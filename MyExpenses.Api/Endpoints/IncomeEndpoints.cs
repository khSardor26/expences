using System.Security.Claims;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Endpoints;

public static class IncomeEndpoints
{
    public static void MapIncomeEndpoints(this IEndpointRouteBuilder app)
    {
        var incomes = app.MapGroup("/api/incomes");

        incomes.MapPost("/", Create);
        incomes.MapPut("/{id}", Update);
        incomes.MapDelete("/{id}", Delete);
        incomes.MapPost("/restore", Restore);
    }

    private static Validator Validate(long amount, string? source, DateOnly date, string? note)
    {
        var validator = new Validator();
        validator.Amount(amount);
        validator.Required("source", source, Limits.Name);
        validator.Date(date);
        validator.Optional("note", note, Limits.Note);
        return validator;
    }

    private static async Task<IResult> Create(IncomeRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = Validate(request.Amount, request.Source, request.Date, request.Note);
        if (!validator.IsValid) return validator.Problem();

        var income = new Income
        {
            UserId = user.UserId(),
            Id = EndpointHelpers.NewId(),
            Amount = request.Amount,
            Source = request.Source!,
            Date = request.Date,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Incomes.Add(income);
        await db.SaveChangesAsync();
        return Results.Ok(IncomeDto.From(income));
    }

    private static async Task<IResult> Update(string id, IncomeRequest request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = Validate(request.Amount, request.Source, request.Date, request.Note);
        if (!validator.IsValid) return validator.Problem();

        var income = await db.Incomes.FindAsync(user.UserId(), id);
        if (income is null) return Results.NotFound();

        income.Amount = request.Amount;
        income.Source = request.Source!;
        income.Date = request.Date;
        income.Note = request.Note;
        await db.SaveChangesAsync();
        return Results.Ok(IncomeDto.From(income));
    }

    private static async Task<IResult> Delete(string id, ClaimsPrincipal user, AppDbContext db)
    {
        var income = await db.Incomes.FindAsync(user.UserId(), id);
        if (income is null) return Results.NotFound();

        db.Incomes.Remove(income);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Undo for a delete: puts the entry back with its original id and creation time.</summary>
    private static async Task<IResult> Restore(IncomeDto request, ClaimsPrincipal user, AppDbContext db)
    {
        var validator = Validate(request.Amount, request.Source, request.Date, request.Note);
        validator.Required("id", request.Id, Limits.Id);
        if (!validator.IsValid) return validator.Problem();

        var userId = user.UserId();
        var existing = await db.Incomes.FindAsync(userId, request.Id);
        if (existing is not null) return Results.Ok(IncomeDto.From(existing));

        var income = new Income
        {
            UserId = userId,
            Id = request.Id,
            Amount = request.Amount,
            Source = request.Source,
            Date = request.Date,
            Note = request.Note,
            CreatedAt = request.CreatedAt.ToUniversalTime(),
        };
        db.Incomes.Add(income);
        await db.SaveChangesAsync();
        return Results.Ok(IncomeDto.From(income));
    }
}
