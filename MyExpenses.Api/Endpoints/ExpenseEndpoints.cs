using System.Security.Claims;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Endpoints;

public static class ExpenseEndpoints
{
    public static void MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var expenses = app.MapGroup("/api/expenses");

        expenses.MapPost("/", Create);
        expenses.MapPut("/{id}", Update);
        expenses.MapDelete("/{id}", Delete);
        expenses.MapPost("/restore", Restore);
    }

    private static async Task<Validator> Validate(
        string userId, long amount, string? categoryId, string? itemId, DateOnly date, string? quantity, string? note, AppDbContext db)
    {
        var validator = new Validator();
        validator.Amount(amount);
        validator.Required("categoryId", categoryId, Limits.Id);
        validator.Date(date);
        validator.Optional("quantity", quantity, Limits.Name);
        validator.Optional("note", note, Limits.Note);
        if (!validator.IsValid) return validator;

        if (await db.Categories.FindAsync(userId, categoryId) is null)
        {
            validator.Add("categoryId", "This category doesn't exist.");
            return validator;
        }

        if (itemId is not null)
        {
            var item = await db.Items.FindAsync(userId, itemId);
            if (item is null || item.CategoryId != categoryId)
                validator.Add("itemId", "This item doesn't belong to the category.");
        }

        return validator;
    }

    private static async Task<IResult> Create(ExpenseRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        var validator = await Validate(
            userId, request.Amount, request.CategoryId, request.ItemId, request.Date, request.Quantity, request.Note, db);
        if (!validator.IsValid) return validator.Problem();

        var expense = new Expense
        {
            UserId = userId,
            Id = EndpointHelpers.NewId(),
            Amount = request.Amount,
            CategoryId = request.CategoryId!,
            ItemId = request.ItemId,
            Date = request.Date,
            Quantity = request.Quantity,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return Results.Ok(ExpenseDto.From(expense));
    }

    private static async Task<IResult> Update(string id, ExpenseRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        var expense = await db.Expenses.FindAsync(userId, id);
        if (expense is null) return Results.NotFound();

        var validator = await Validate(
            userId, request.Amount, request.CategoryId, request.ItemId, request.Date, request.Quantity, request.Note, db);
        if (!validator.IsValid) return validator.Problem();

        expense.Amount = request.Amount;
        expense.CategoryId = request.CategoryId!;
        expense.ItemId = request.ItemId;
        expense.Date = request.Date;
        expense.Quantity = request.Quantity;
        expense.Note = request.Note;
        await db.SaveChangesAsync();
        return Results.Ok(ExpenseDto.From(expense));
    }

    private static async Task<IResult> Delete(string id, ClaimsPrincipal user, AppDbContext db)
    {
        var expense = await db.Expenses.FindAsync(user.UserId(), id);
        if (expense is null) return Results.NotFound();

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Undo for a delete: puts the entry back with its original id and creation time.</summary>
    private static async Task<IResult> Restore(ExpenseDto request, ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        var validator = await Validate(
            userId, request.Amount, request.CategoryId, request.ItemId, request.Date, request.Quantity, request.Note, db);
        validator.Required("id", request.Id, Limits.Id);
        if (!validator.IsValid) return validator.Problem();

        var existing = await db.Expenses.FindAsync(userId, request.Id);
        if (existing is not null) return Results.Ok(ExpenseDto.From(existing));

        var expense = new Expense
        {
            UserId = userId,
            Id = request.Id,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            ItemId = request.ItemId,
            Date = request.Date,
            Quantity = request.Quantity,
            Note = request.Note,
            CreatedAt = request.CreatedAt.ToUniversalTime(),
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return Results.Ok(ExpenseDto.From(expense));
    }
}
