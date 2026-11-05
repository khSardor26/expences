using MyExpenses.Api.Data;

namespace MyExpenses.Api;

// The JSON the API speaks. These match the types in src/lib/types.ts.

public static class Limits
{
    public const int Id = 64;
    public const int Name = 100;
    public const int Note = 1000;
    public const int Icon = 16;
    public const int Color = 16;
    public const int CurrencyCode = 8;
    public const int CurrencySymbol = 8;
    public const int MaxDecimals = 4;

    /// <summary>
    /// The most one entry can be (same limit as src/lib/money.ts). It leaves room for
    /// totals to stay within what JavaScript can hold exactly (Number.MAX_SAFE_INTEGER).
    /// </summary>
    public const long MaxAmount = 10_000_000_000_000;
}

public record CredentialsRequest(string? Email, string? Password);
public record UserDto(string Email);

public record LoginResponse(string? Email, bool RequiresCode, string? ChallengeId, bool? Delivered);
public record VerifyLoginRequest(string? ChallengeId, string? Code, string? RecoveryCode);
public record ChallengeRequest(string? ChallengeId);
public record ResendResponse(bool Delivered);

public record SecurityStatusDto(bool TelegramAvailable, string Telegram, int RecoveryCodesLeft);
public record ConnectTelegramResponse(string Url);
public record CodeRequest(string? Code);
public record RecoveryCodesDto(List<string> RecoveryCodes);
public record DisableTelegramRequest(string? Password, string? Code, string? RecoveryCode);

public record IncomeRequest(long Amount, string? Source, DateOnly Date, string? Note);
public record IncomeDto(string Id, long Amount, string Source, DateOnly Date, string? Note, DateTimeOffset CreatedAt)
{
    public static IncomeDto From(Income i) => new(i.Id, i.Amount, i.Source, i.Date, i.Note, i.CreatedAt);
}

public record ExpenseRequest(long Amount, string? CategoryId, string? ItemId, DateOnly Date, string? Quantity, string? Note);
public record ExpenseDto(
    string Id, long Amount, string CategoryId, string? ItemId, DateOnly Date, string? Quantity, string? Note, DateTimeOffset CreatedAt)
{
    public static ExpenseDto From(Expense e) =>
        new(e.Id, e.Amount, e.CategoryId, e.ItemId, e.Date, e.Quantity, e.Note, e.CreatedAt);
}

public record CategoryRequest(string? Name, string? Icon, string? Color);
public record CategoryDto(string Id, string Name, string Icon, string Color, int Order, bool Archived)
{
    public static CategoryDto From(Category c) => new(c.Id, c.Name, c.Icon, c.Color, c.Order, c.Archived);
}

public record ArchiveCategoryRequest(string? MoveExpensesTo);
public record ReorderRequest(List<string>? Ids);

public record ItemRequest(string? CategoryId, string? Name);
public record ItemDto(string Id, string CategoryId, string Name, int Order, bool Archived)
{
    public static ItemDto From(Item i) => new(i.Id, i.CategoryId, i.Name, i.Order, i.Archived);
}

public record CurrencyDto(string? Code, string? Symbol, int Decimals);
public record SettingsDto(CurrencyDto? Currency, string? PeriodType, int WeekStartsOn, int MonthStartDay, string? Theme)
{
    public static SettingsDto From(UserSettings s) => new(
        new CurrencyDto(s.CurrencyCode, s.CurrencySymbol, s.CurrencyDecimals),
        s.PeriodType, s.WeekStartsOn, s.MonthStartDay, s.Theme);
}

public record IncomeSourcesRequest(List<string>? Sources);

public record AppDataDto(
    List<IncomeDto>? Incomes,
    List<ExpenseDto>? Expenses,
    List<CategoryDto>? Categories,
    List<ItemDto>? Items,
    List<string>? IncomeSources,
    SettingsDto? Settings);
