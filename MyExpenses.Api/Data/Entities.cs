using Microsoft.AspNetCore.Identity;

namespace MyExpenses.Api.Data;

public class AppUser : IdentityUser
{
    /// <summary>The Telegram chat linked to this account, once the user has pressed Start in the bot.</summary>
    public long? TelegramChatId { get; set; }

    /// <summary>True once a code has been confirmed: sign-in now needs a code as well as the password.</summary>
    public bool TelegramEnabled { get; set; }
}

/// <summary>A one-time code sent to Telegram, waiting to be entered. Only its hash is stored.</summary>
public class SecurityChallenge
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string Purpose { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public int Attempts { get; set; }
}

/// <summary>A one-time token in the link that connects a Telegram chat to an account.</summary>
public class TelegramLink
{
    public required string UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A one-use code for signing in without Telegram. Only its hash is stored.</summary>
public class RecoveryCode
{
    public required string UserId { get; set; }
    public required string CodeHash { get; set; }
}

// Every table's primary key starts with UserId, and foreign keys include it too.
// A row can only be fetched by giving its owner, and an expense can never point
// at another user's category: the database itself rejects it.

public class Income
{
    public required string UserId { get; set; }
    public required string Id { get; set; }
    public long Amount { get; set; } // integer, in the currency's smallest unit
    public required string Source { get; set; }
    public DateOnly Date { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Expense
{
    public required string UserId { get; set; }
    public required string Id { get; set; }
    public long Amount { get; set; } // integer, in the currency's smallest unit
    public required string CategoryId { get; set; }
    public string? ItemId { get; set; }
    public DateOnly Date { get; set; }
    public string? Quantity { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Category
{
    public required string UserId { get; set; }
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Icon { get; set; }
    public required string Color { get; set; }
    public int Order { get; set; }
    public bool Archived { get; set; }
}

public class Item
{
    public required string UserId { get; set; }
    public required string Id { get; set; }
    public required string CategoryId { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
    public bool Archived { get; set; }
}

public class UserSettings
{
    public required string UserId { get; set; }
    public required string CurrencyCode { get; set; }
    public required string CurrencySymbol { get; set; }
    public int CurrencyDecimals { get; set; }
    public required string PeriodType { get; set; }
    public int WeekStartsOn { get; set; }
    public int MonthStartDay { get; set; }
    public required string Theme { get; set; }
    public List<string> IncomeSources { get; set; } = [];
}
