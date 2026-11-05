using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/categories");

        categories.MapPost("/", Create);
        categories.MapPut("/order", Reorder);
        categories.MapPut("/{id}", Update);
        categories.MapPost("/{id}/archive", Archive);
    }

    private static Validator Validate(CategoryRequest request)
    {
        var validator = new Validator();
        validator.Required("name", request.Name, Limits.Name);
        validator.Required("icon", request.Icon, Limits.Icon);
        validator.Required("color", request.Color, Limits.Color);
        return validator;
    }

    private static async Task<IResult> Create(CategoryRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = Validate(request);
        if (!validator.IsValid) return validator.Problem();

        var userId = user.UserId();
        var lastOrder = await db.Categories.Where(c => c.UserId == userId).MaxAsync(c => (int?)c.Order) ?? -1;
        var category = new Category
        {
            UserId = userId,
            Id = EndpointHelpers.NewId(),
            Name = request.Name!,
            Icon = request.Icon!,
            Color = request.Color!,
            Order = lastOrder + 1,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return Results.Ok(CategoryDto.From(category));
    }

    private static async Task<IResult> Update(string id, CategoryRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = Validate(request);
        if (!validator.IsValid) return validator.Problem();

        var category = await db.Categories.FindAsync(user.UserId(), id);
        if (category is null) return Results.NotFound();

        category.Name = request.Name!;
        category.Icon = request.Icon!;
        category.Color = request.Color!;
        await db.SaveChangesAsync();
        return Results.Ok(CategoryDto.From(category));
    }

    private static async Task<IResult> Reorder(ReorderRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        var categories = await db.Categories.Where(c => c.UserId == userId).ToDictionaryAsync(c => c.Id);

        var ids = request.Ids ?? [];
        for (var order = 0; order < ids.Count; order++)
        {
            if (!categories.TryGetValue(ids[order], out var category)) return Results.NotFound();
            category.Order = order;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Soft delete. Expenses are never silently affected: if the category has any,
    /// the caller must say where they go, or this returns 409 with no changes made.
    /// </summary>
    private static async Task<IResult> Archive(string id, ArchiveCategoryRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        if (id == SeedData.OtherCategoryId)
        {
            return Results.Problem(
                "The Other category can't be archived.", statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = user.UserId();
        var category = await db.Categories.FindAsync(userId, id);
        if (category is null) return Results.NotFound();

        var expenses = db.Expenses.Where(e => e.UserId == userId && e.CategoryId == id);

        if (request.MoveExpensesTo is { } targetId)
        {
            var target = await db.Categories.FindAsync(userId, targetId);
            if (target is null || target.Archived || targetId == id)
            {
                var validator = new Validator();
                validator.Add("moveExpensesTo", "Choose another active category.");
                return validator.Problem();
            }
        }
        else if (await expenses.AnyAsync())
        {
            return Results.Problem(
                "This category still has expenses. Choose where to move them.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Items belong to the old category, so moved expenses lose their item.
        if (request.MoveExpensesTo is { } destination)
        {
            await expenses.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.CategoryId, destination)
                .SetProperty(e => e.ItemId, (string?)null));
        }

        category.Archived = true;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(CategoryDto.From(category));
    }
}
