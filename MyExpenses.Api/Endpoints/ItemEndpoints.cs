using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Endpoints;

public static class ItemEndpoints
{
    public static void MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var items = app.MapGroup("/api/items");

        items.MapPost("/", Create);
        items.MapPut("/order", Reorder);
        items.MapPut("/{id}", Rename);
        items.MapPost("/{id}/archive", Archive);
    }

    private static async Task<IResult> Create(ItemRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = new Validator();
        validator.Required("name", request.Name, Limits.Name);
        validator.Required("categoryId", request.CategoryId, Limits.Id);
        if (!validator.IsValid) return validator.Problem();

        var userId = user.UserId();
        if (await db.Categories.FindAsync(userId, request.CategoryId) is null)
        {
            validator.Add("categoryId", "This category doesn't exist.");
            return validator.Problem();
        }

        var lastOrder = await db.Items
            .Where(i => i.UserId == userId && i.CategoryId == request.CategoryId)
            .MaxAsync(i => (int?)i.Order) ?? -1;
        var item = new Item
        {
            UserId = userId,
            Id = EndpointHelpers.NewId(),
            CategoryId = request.CategoryId!,
            Name = request.Name!,
            Order = lastOrder + 1,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return Results.Ok(ItemDto.From(item));
    }

    private static async Task<IResult> Rename(string id, ItemRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = new Validator();
        validator.Required("name", request.Name, Limits.Name);
        if (!validator.IsValid) return validator.Problem();

        var item = await db.Items.FindAsync(user.UserId(), id);
        if (item is null) return Results.NotFound();

        item.Name = request.Name!;
        await db.SaveChangesAsync();
        return Results.Ok(ItemDto.From(item));
    }

    private static async Task<IResult> Reorder(ReorderRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        var items = await db.Items.Where(i => i.UserId == userId).ToDictionaryAsync(i => i.Id);

        var ids = request.Ids ?? [];
        for (var order = 0; order < ids.Count; order++)
        {
            if (!items.TryGetValue(ids[order], out var item)) return Results.NotFound();
            item.Order = order;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Soft delete: past expenses keep showing this item's name.</summary>
    private static async Task<IResult> Archive(string id, ClaimsPrincipal user, AppDbContext db)
    {
        var item = await db.Items.FindAsync(user.UserId(), id);
        if (item is null) return Results.NotFound();

        item.Archived = true;
        await db.SaveChangesAsync();
        return Results.Ok(ItemDto.From(item));
    }
}
