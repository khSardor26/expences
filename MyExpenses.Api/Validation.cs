using System.Security.Claims;

namespace MyExpenses.Api;

/// <summary>Collects field errors for a request. Empty means the request is valid.</summary>
public class Validator
{
    public Dictionary<string, string[]> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;

    public void Add(string field, string message) => Errors[field] = [message];

    public void Amount(long amount)
    {
        if (amount is <= 0 or > Limits.MaxAmount) Add("amount", "Amount must be a positive whole number.");
    }

    public void Date(DateOnly date)
    {
        if (date == default) Add("date", "A date is required.");
    }

    public void Required(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, "This field is required.");
        else Optional(field, value, maxLength);
    }

    public void Optional(string field, string? value, int maxLength)
    {
        if (value?.Length > maxLength) Add(field, $"Must be {maxLength} characters or fewer.");
    }

    public void Settings(SettingsDto? settings)
    {
        if (settings is null)
        {
            Add("settings", "Settings are required.");
            return;
        }

        Required("currency.code", settings.Currency?.Code, Limits.CurrencyCode);
        Required("currency.symbol", settings.Currency?.Symbol, Limits.CurrencySymbol);
        if (settings.Currency?.Decimals is null or < 0 or > Limits.MaxDecimals)
            Add("currency.decimals", $"Must be between 0 and {Limits.MaxDecimals}.");
        if (settings.PeriodType is not ("week" or "month")) Add("periodType", "Must be week or month.");
        if (settings.WeekStartsOn is not (0 or 1)) Add("weekStartsOn", "Must be 0 (Sunday) or 1 (Monday).");
        if (settings.MonthStartDay is < 1 or > 28) Add("monthStartDay", "Must be between 1 and 28.");
        if (settings.Theme is not ("light" or "dark" or "system")) Add("theme", "Must be light, dark or system.");
    }

    public void IncomeSources(List<string>? sources)
    {
        if (sources is null || sources.Count == 0)
        {
            Add("sources", "At least one income source is required.");
            return;
        }

        if (sources.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > Limits.Name))
            Add("sources", $"Each source must be 1 to {Limits.Name} characters.");
    }

    public IResult Problem() => Results.ValidationProblem(Errors);
}

public static class EndpointHelpers
{
    /// <summary>The signed-in user. All endpoints but sign-in require authentication.</summary>
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Endpoint reached without an authenticated user.");

    public static string NewId() => Guid.NewGuid().ToString();
}
