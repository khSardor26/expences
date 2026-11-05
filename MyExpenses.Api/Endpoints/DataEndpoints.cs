using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Endpoints;

public static class DataEndpoints
{
    public static void MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/data", GetAll);
        app.MapPost("/api/data/import", Import);
        app.MapDelete("/api/data", Reset);
        app.MapPut("/api/settings", UpdateSettings);
        app.MapPut("/api/income-sources", UpdateIncomeSources);
    }

    /// <summary>Everything the signed-in user has. The app loads this after sign-in.</summary>
    private static async Task<IResult> GetAll(ClaimsPrincipal user, AppDbContext db)
    {
        var data = await ReadAll(user.UserId(), db);
        return Results.Ok(data);
    }

    private static async Task<AppDataDto> ReadAll(string userId, AppDbContext db)
    {
        var incomes = await db.Incomes.AsNoTracking().Where(i => i.UserId == userId).ToListAsync();
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();
        var categories = await db.Categories.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Order).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.UserId == userId).OrderBy(i => i.Order).ToListAsync();
        var settings = await db.UserSettings.AsNoTracking().SingleAsync(s => s.UserId == userId);

        return new AppDataDto(
            incomes.Select(IncomeDto.From).ToList(),
            expenses.Select(ExpenseDto.From).ToList(),
            categories.Select(CategoryDto.From).ToList(),
            items.Select(ItemDto.From).ToList(),
            settings.IncomeSources,
            SettingsDto.From(settings));
    }

    private static async Task<IResult> UpdateSettings(SettingsDto request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = new Validator();
        validator.Settings(request);
        if (!validator.IsValid) return validator.Problem();

        var settings = await db.UserSettings.SingleAsync(s => s.UserId == user.UserId());
        ApplySettings(settings, request);
        await db.SaveChangesAsync();
        return Results.Ok(SettingsDto.From(settings));
    }

    private static async Task<IResult> UpdateIncomeSources(IncomeSourcesRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = new Validator();
        validator.IncomeSources(request.Sources);
        if (!validator.IsValid) return validator.Problem();

        var settings = await db.UserSettings.SingleAsync(s => s.UserId == user.UserId());
        settings.IncomeSources = request.Sources!;
        await db.SaveChangesAsync();
        return Results.Ok(settings.IncomeSources);
    }

    /// <summary>Replaces everything with the backup, all or nothing.</summary>
    private static async Task<IResult> Import(AppDataDto backup, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = ValidateBackup(backup);
        if (!validator.IsValid) return validator.Problem();

        var userId = user.UserId();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DeleteEverything(userId, db);

        db.Categories.AddRange(backup.Categories!.Select(c => new Category
        {
            UserId = userId, Id = c.Id, Name = c.Name, Icon = c.Icon, Color = c.Color, Order = c.Order, Archived = c.Archived,
        }));
        db.Items.AddRange(backup.Items!.Select(i => new Item
        {
            UserId = userId, Id = i.Id, CategoryId = i.CategoryId, Name = i.Name, Order = i.Order, Archived = i.Archived,
        }));
        db.Incomes.AddRange(backup.Incomes!.Select(i => new Income
        {
            UserId = userId, Id = i.Id, Amount = i.Amount, Source = i.Source, Date = i.Date, Note = i.Note,
            CreatedAt = i.CreatedAt.ToUniversalTime(),
        }));
        db.Expenses.AddRange(backup.Expenses!.Select(e => new Expense
        {
            UserId = userId, Id = e.Id, Amount = e.Amount, CategoryId = e.CategoryId, ItemId = e.ItemId, Date = e.Date,
            Quantity = e.Quantity, Note = e.Note, CreatedAt = e.CreatedAt.ToUniversalTime(),
        }));

        var settings = new UserSettings
        {
            UserId = userId,
            CurrencyCode = "",
            CurrencySymbol = "",
            PeriodType = "",
            Theme = "",
            IncomeSources = backup.IncomeSources!,
        };
        ApplySettings(settings, backup.Settings!);
        db.UserSettings.Add(settings);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(await ReadAll(userId, db));
    }

    /// <summary>Deletes all of the user's data and starts again from the defaults. The account stays.</summary>
    private static async Task<IResult> Reset(ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DeleteEverything(userId, db);
        SeedData.AddTo(db, userId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(await ReadAll(userId, db));
    }

    private static async Task DeleteEverything(string userId, AppDbContext db)
    {
        await db.Expenses.Where(e => e.UserId == userId).ExecuteDeleteAsync();
        await db.Incomes.Where(i => i.UserId == userId).ExecuteDeleteAsync();
        await db.Items.Where(i => i.UserId == userId).ExecuteDeleteAsync();
        await db.Categories.Where(c => c.UserId == userId).ExecuteDeleteAsync();
        await db.UserSettings.Where(s => s.UserId == userId).ExecuteDeleteAsync();
    }

    private static void ApplySettings(UserSettings target, SettingsDto source)
    {
        target.CurrencyCode = source.Currency!.Code!;
        target.CurrencySymbol = source.Currency.Symbol!;
        target.CurrencyDecimals = source.Currency.Decimals;
        target.PeriodType = source.PeriodType!;
        target.WeekStartsOn = source.WeekStartsOn;
        target.MonthStartDay = source.MonthStartDay;
        target.Theme = source.Theme!;
    }

    private static Validator ValidateBackup(AppDataDto backup)
    {
        var validator = new Validator();
        validator.Settings(backup.Settings);
        validator.IncomeSources(backup.IncomeSources);

        if (backup.Incomes is null || backup.Expenses is null || backup.Categories is null || backup.Items is null)
        {
            validator.Add("backup", "The backup is incomplete.");
            return validator;
        }

        foreach (var income in backup.Incomes)
        {
            validator.Required("incomes.id", income.Id, Limits.Id);
            validator.Amount(income.Amount);
            validator.Required("incomes.source", income.Source, Limits.Name);
            validator.Date(income.Date);
            validator.Optional("incomes.note", income.Note, Limits.Note);
        }

        foreach (var category in backup.Categories)
        {
            validator.Required("categories.id", category.Id, Limits.Id);
            validator.Required("categories.name", category.Name, Limits.Name);
            validator.Required("categories.icon", category.Icon, Limits.Icon);
            validator.Required("categories.color", category.Color, Limits.Color);
        }

        var categoryIds = backup.Categories.Select(c => c.Id).ToHashSet();
        var itemCategories = new Dictionary<string, string>();
        foreach (var item in backup.Items)
        {
            validator.Required("items.id", item.Id, Limits.Id);
            validator.Required("items.name", item.Name, Limits.Name);
            if (!categoryIds.Contains(item.CategoryId))
                validator.Add("items.categoryId", "An item refers to a category that isn't in the backup.");
            itemCategories[item.Id ?? ""] = item.CategoryId;
        }

        foreach (var expense in backup.Expenses)
        {
            validator.Required("expenses.id", expense.Id, Limits.Id);
            validator.Amount(expense.Amount);
            validator.Date(expense.Date);
            validator.Optional("expenses.quantity", expense.Quantity, Limits.Name);
            validator.Optional("expenses.note", expense.Note, Limits.Note);
            if (!categoryIds.Contains(expense.CategoryId))
                validator.Add("expenses.categoryId", "An expense refers to a category that isn't in the backup.");
            if (expense.ItemId is not null &&
                (!itemCategories.TryGetValue(expense.ItemId, out var itemCategory) || itemCategory != expense.CategoryId))
                validator.Add("expenses.itemId", "An expense refers to an item that isn't in its category.");
        }

        AddDuplicateError(validator, "incomes.id", backup.Incomes.Select(i => i.Id));
        AddDuplicateError(validator, "expenses.id", backup.Expenses.Select(e => e.Id));
        AddDuplicateError(validator, "categories.id", backup.Categories.Select(c => c.Id));
        AddDuplicateError(validator, "items.id", backup.Items.Select(i => i.Id));

        if (!categoryIds.Contains(SeedData.OtherCategoryId))
            validator.Add("categories", "The backup is missing the Other category.");

        return validator;
    }

    private static void AddDuplicateError(Validator validator, string field, IEnumerable<string> ids)
    {
        var all = ids.ToList();
        if (all.Distinct().Count() != all.Count) validator.Add(field, "The backup contains duplicate ids.");
    }
}
