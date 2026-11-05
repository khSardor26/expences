using System.Text.RegularExpressions;

namespace MyExpenses.Api.Data;

/// <summary>What every new account starts with. All of it can be edited.</summary>
public static partial class SeedData
{
    /// <summary>The fallback for moved expenses, so this category can never be archived.</summary>
    public const string OtherCategoryId = "other";

    private static readonly (string Id, string Name, string Icon, string Color, string[] Items)[] Categories =
    [
        ("groceries", "Groceries", "🛒", "#2f855a",
            ["Macaroni/Pasta", "Chicken", "Meat", "Rice", "Bread", "Vegetables", "Fruits", "Dairy", "Eggs", "Oil", "Sugar & Tea"]),
        ("eating-out", "Eating out", "🍽", "#c05621", ["Cafe", "Fast food", "Coffee"]),
        ("transport", "Transport", "🚕", "#b7791f", ["Taxi", "Metro/Bus", "Fuel"]),
        ("home-bills", "Home & Bills", "🏠", "#2b6cb0", ["Rent", "Electricity", "Gas", "Water", "Internet", "Mobile"]),
        ("health", "Health", "💊", "#c53030", ["Pharmacy", "Doctor"]),
        ("shopping", "Shopping", "👕", "#6b46c1", ["Clothes", "Household items"]),
        ("education", "Education", "🎓", "#2c7a7b", ["Courses", "Books"]),
        ("entertainment", "Entertainment", "🎉", "#b83280", ["Subscriptions", "Going out"]),
        ("gifts-family", "Gifts & Family", "🎁", "#975a16", []),
        (OtherCategoryId, "Other", "📦", "#4a5568", []),
    ];

    /// <summary>Adds the default categories, items and settings. The caller saves.</summary>
    public static void AddTo(AppDbContext db, string userId)
    {
        for (var order = 0; order < Categories.Length; order++)
        {
            var (id, name, icon, color, items) = Categories[order];
            db.Categories.Add(new Category
            {
                UserId = userId, Id = id, Name = name, Icon = icon, Color = color, Order = order,
            });
            db.Items.AddRange(items.Select((itemName, itemOrder) => new Item
            {
                UserId = userId,
                Id = $"{id}-{Slug(itemName)}",
                CategoryId = id,
                Name = itemName,
                Order = itemOrder,
            }));
        }

        db.UserSettings.Add(new UserSettings
        {
            UserId = userId,
            CurrencyCode = "UZS",
            CurrencySymbol = "so'm",
            CurrencyDecimals = 0,
            PeriodType = "month",
            WeekStartsOn = 1,
            MonthStartDay = 1,
            Theme = "system",
            IncomeSources = ["Salary", "Freelance", "Gift", "Other"],
        });
    }

    private static string Slug(string name) => NonAlphanumeric().Replace(name.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
