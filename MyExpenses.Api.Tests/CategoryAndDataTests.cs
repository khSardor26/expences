using System.Net;
using System.Net.Http.Json;

namespace MyExpenses.Api.Tests;

[Collection("api")]
public class CategoryAndDataTests(ApiFactory api)
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static Task<HttpResponseMessage> AddExpense(HttpClient client, string categoryId, string? itemId = null) =>
        client.PostAsJsonAsync("/api/expenses", new ExpenseRequest(1_000, categoryId, itemId, Today, null, null));

    [Fact]
    public async Task Categories_can_be_added_and_renamed()
    {
        var client = await api.NewUserAsync();

        var created = await (await client.PostAsJsonAsync(
            "/api/categories", new CategoryRequest("Pets", "🐈", "#123456"))).ReadAsync<CategoryDto>();
        Assert.Equal(10, created.Order);

        var renamed = await (await client.PutAsJsonAsync(
            $"/api/categories/{created.Id}", new CategoryRequest("Cats", "🐱", "#654321"))).ReadAsync<CategoryDto>();
        Assert.Equal("Cats", renamed.Name);
        Assert.Equal(10, renamed.Order);
    }

    [Fact]
    public async Task Reordering_categories_changes_the_order_returned()
    {
        var client = await api.NewUserAsync();
        var ids = (await client.GetDataAsync()).Categories!.Select(c => c.Id).Reverse().ToList();

        var response = await client.PutAsJsonAsync("/api/categories/order", new ReorderRequest(ids));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ids, (await client.GetDataAsync()).Categories!.Select(c => c.Id));
    }

    [Fact]
    public async Task Archiving_a_category_with_expenses_requires_a_destination()
    {
        var client = await api.NewUserAsync();
        await AddExpense(client, "groceries", "groceries-chicken");

        var response = await client.PostAsJsonAsync("/api/categories/groceries/archive", new ArchiveCategoryRequest(null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var data = await client.GetDataAsync();
        Assert.False(data.Categories!.Single(c => c.Id == "groceries").Archived);
        Assert.Equal("groceries", data.Expenses!.Single().CategoryId);
    }

    [Fact]
    public async Task Archiving_moves_expenses_and_keeps_them()
    {
        var client = await api.NewUserAsync();
        await AddExpense(client, "groceries", "groceries-chicken");
        await AddExpense(client, "groceries");
        await AddExpense(client, "transport", "transport-taxi");

        var response = await client.PostAsJsonAsync("/api/categories/groceries/archive", new ArchiveCategoryRequest("other"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await client.GetDataAsync();
        Assert.True(data.Categories!.Single(c => c.Id == "groceries").Archived);
        Assert.Equal(3, data.Expenses!.Count);
        Assert.Equal(2, data.Expenses.Count(e => e.CategoryId == "other" && e.ItemId is null));
        Assert.Single(data.Expenses, e => e is { CategoryId: "transport", ItemId: "transport-taxi" });
    }

    [Fact]
    public async Task Archiving_an_empty_category_needs_no_destination()
    {
        var client = await api.NewUserAsync();
        var response = await client.PostAsJsonAsync("/api/categories/health/archive", new ArchiveCategoryRequest(null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("groceries")] // the category being archived
    [InlineData("missing")]
    public async Task Archive_destination_must_be_another_real_category(string destination)
    {
        var client = await api.NewUserAsync();
        await AddExpense(client, "groceries");

        var response = await client.PostAsJsonAsync("/api/categories/groceries/archive", new ArchiveCategoryRequest(destination));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await client.GetDataAsync()).Categories!.Single(c => c.Id == "groceries").Archived);
    }

    [Fact]
    public async Task The_other_category_cannot_be_archived()
    {
        var client = await api.NewUserAsync();
        var response = await client.PostAsJsonAsync("/api/categories/other/archive", new ArchiveCategoryRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Items_can_be_added_renamed_reordered_and_archived()
    {
        var client = await api.NewUserAsync();

        var item = await (await client.PostAsJsonAsync("/api/items", new ItemRequest("gifts-family", "Birthday"))).ReadAsync<ItemDto>();
        Assert.Equal(0, item.Order);

        var renamed = await (await client.PutAsJsonAsync($"/api/items/{item.Id}", new ItemRequest(null, "Birthdays"))).ReadAsync<ItemDto>();
        Assert.Equal("Birthdays", renamed.Name);

        var second = await (await client.PostAsJsonAsync("/api/items", new ItemRequest("gifts-family", "Weddings"))).ReadAsync<ItemDto>();
        Assert.Equal(1, second.Order);
        await client.PutAsJsonAsync("/api/items/order", new ReorderRequest([second.Id, item.Id]));
        Assert.Equal(
            [second.Id, item.Id],
            (await client.GetDataAsync()).Items!.Where(i => i.CategoryId == "gifts-family").Select(i => i.Id));

        var archived = await (await client.PostAsync($"/api/items/{item.Id}/archive", null)).ReadAsync<ItemDto>();
        Assert.True(archived.Archived);
    }

    [Fact]
    public async Task Archiving_an_item_keeps_its_expenses()
    {
        var client = await api.NewUserAsync();
        await AddExpense(client, "groceries", "groceries-chicken");

        await client.PostAsync("/api/items/groceries-chicken/archive", null);

        Assert.Equal("groceries-chicken", (await client.GetDataAsync()).Expenses!.Single().ItemId);
    }

    [Fact]
    public async Task Settings_can_be_saved()
    {
        var client = await api.NewUserAsync();
        var settings = new SettingsDto(new CurrencyDto("USD", "$", 2), "week", 0, 10, "dark");

        (await client.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();

        var saved = (await client.GetDataAsync()).Settings!;
        Assert.Equal("USD", saved.Currency!.Code);
        Assert.Equal(2, saved.Currency.Decimals);
        Assert.Equal("week", saved.PeriodType);
        Assert.Equal(0, saved.WeekStartsOn);
        Assert.Equal(10, saved.MonthStartDay);
        Assert.Equal("dark", saved.Theme);
    }

    public static TheoryData<SettingsDto> BadSettings() => new()
    {
        new SettingsDto(new CurrencyDto("UZS", "so'm", 0), "month", 1, 0, "system"),
        new SettingsDto(new CurrencyDto("UZS", "so'm", 0), "month", 1, 29, "system"),
        new SettingsDto(new CurrencyDto("UZS", "so'm", 0), "month", 2, 1, "system"),
        new SettingsDto(new CurrencyDto("UZS", "so'm", 0), "year", 1, 1, "system"),
        new SettingsDto(new CurrencyDto("UZS", "so'm", 0), "month", 1, 1, "blue"),
        new SettingsDto(new CurrencyDto("UZS", "so'm", -1), "month", 1, 1, "system"),
        new SettingsDto(new CurrencyDto("UZS", "so'm", 5), "month", 1, 1, "system"),
        new SettingsDto(new CurrencyDto("", "so'm", 0), "month", 1, 1, "system"),
        new SettingsDto(null, "month", 1, 1, "system"),
    };

    [Theory]
    [MemberData(nameof(BadSettings))]
    public async Task Bad_settings_are_rejected(SettingsDto settings)
    {
        var client = await api.NewUserAsync();
        var response = await client.PutAsJsonAsync("/api/settings", settings);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Income_sources_can_be_edited_but_not_emptied()
    {
        var client = await api.NewUserAsync();

        (await client.PutAsJsonAsync("/api/income-sources", new IncomeSourcesRequest(["Salary", "Rent"]))).EnsureSuccessStatusCode();
        Assert.Equal(["Salary", "Rent"], (await client.GetDataAsync()).IncomeSources);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/income-sources", new IncomeSourcesRequest([]))).StatusCode);
    }

    [Fact]
    public async Task A_backup_can_be_imported_into_another_account_exactly()
    {
        var source = await api.NewUserAsync();
        await source.PostAsJsonAsync("/api/incomes", new IncomeRequest(5_000_000, "Salary", Today, "note"));
        await AddExpense(source, "groceries", "groceries-chicken");
        await source.PostAsJsonAsync("/api/categories/health/archive", new ArchiveCategoryRequest(null));
        await source.PutAsJsonAsync("/api/settings", new SettingsDto(new CurrencyDto("USD", "$", 2), "week", 0, 10, "dark"));
        var backup = await source.GetDataAsync();

        var target = await api.NewUserAsync();
        await AddExpense(target, "transport"); // must be replaced by the import

        (await target.PostAsJsonAsync("/api/data/import", backup)).EnsureSuccessStatusCode();

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(backup),
            System.Text.Json.JsonSerializer.Serialize(await target.GetDataAsync()));
    }

    [Fact]
    public async Task A_large_backup_restores_every_field_of_every_entry()
    {
        var source = await api.NewUserAsync();
        for (var day = 1; day <= 20; day++)
        {
            var date = new DateOnly(2026, 11, day);
            await source.PostAsJsonAsync("/api/expenses", new ExpenseRequest(day * 100, "groceries", "groceries-chicken", date, "1 kg", $"note {day}"));
            await source.PostAsJsonAsync("/api/expenses", new ExpenseRequest(day * 7, "other", null, date, null, null));
            await source.PostAsJsonAsync("/api/incomes", new IncomeRequest(day * 1000, "Salary", date, null));
        }
        var added = await (await source.PostAsJsonAsync("/api/items", new ItemRequest("groceries", "Lentils"))).ReadAsync<ItemDto>();
        await source.PutAsJsonAsync($"/api/items/{added.Id}", new ItemRequest(null, "Red lentils"));
        await source.PostAsync("/api/items/groceries-meat/archive", null);
        await source.PostAsync("/api/items/groceries-chicken/archive", null);
        await source.PutAsJsonAsync("/api/items/order", new ReorderRequest(["groceries-rice", "groceries-bread"]));
        await source.PostAsJsonAsync("/api/categories/health/archive", new ArchiveCategoryRequest(null));
        var backup = await source.GetDataAsync();

        var target = await api.NewUserAsync();
        await AddExpense(target, "transport");
        (await target.PostAsJsonAsync("/api/data/import", backup)).EnsureSuccessStatusCode();
        var restored = await target.GetDataAsync();

        // Row order isn't part of the data: every screen sorts for itself.
        Assert.Equal(backup.Expenses!.OrderBy(e => e.Id), restored.Expenses!.OrderBy(e => e.Id));
        Assert.Equal(backup.Incomes!.OrderBy(i => i.Id), restored.Incomes!.OrderBy(i => i.Id));
        Assert.Equal(backup.Categories!.OrderBy(c => c.Id), restored.Categories!.OrderBy(c => c.Id));
        Assert.Equal(backup.Items!.OrderBy(i => i.Id), restored.Items!.OrderBy(i => i.Id));
        Assert.Equal(backup.IncomeSources, restored.IncomeSources);
        Assert.Equal(40, restored.Expenses!.Count);
    }

    [Fact]
    public async Task A_bad_backup_changes_nothing()
    {
        var client = await api.NewUserAsync();
        await AddExpense(client, "groceries");
        var before = await client.GetDataAsync();

        var orphan = before with
        {
            Expenses = [before.Expenses!.Single() with { CategoryId = "not-in-backup" }],
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/data/import", orphan)).StatusCode);

        var noOther = before with { Categories = before.Categories!.Where(c => c.Id != "other").ToList() };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/data/import", noOther)).StatusCode);

        var duplicate = before with { Expenses = [before.Expenses![0], before.Expenses[0]] };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/data/import", duplicate)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/data/import", new { })).StatusCode);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(before),
            System.Text.Json.JsonSerializer.Serialize(await client.GetDataAsync()));
    }

    [Fact]
    public async Task Reset_returns_to_the_defaults_and_keeps_the_account()
    {
        var client = await api.NewUserAsync();
        await AddExpense(client, "groceries");
        await client.PostAsJsonAsync("/api/incomes", new IncomeRequest(100, "Salary", Today, null));
        await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Pets", "🐈", "#123456"));

        (await client.DeleteAsync("/api/data")).EnsureSuccessStatusCode();

        var data = await client.GetDataAsync();
        Assert.Empty(data.Expenses!);
        Assert.Empty(data.Incomes!);
        Assert.Equal(10, data.Categories!.Count);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
