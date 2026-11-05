using System.Net;
using System.Net.Http.Json;

namespace MyExpenses.Api.Tests;

[Collection("api")]
public class EntryTests(ApiFactory api)
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public async Task Income_can_be_added_edited_deleted_and_restored()
    {
        var client = await api.NewUserAsync();

        var created = await (await client.PostAsJsonAsync(
            "/api/incomes", new IncomeRequest(5_000_000, "Salary", new DateOnly(2026, 11, 1), "November"))).ReadAsync<IncomeDto>();
        Assert.False(string.IsNullOrEmpty(created.Id));
        Assert.Equal(new DateOnly(2026, 11, 1), created.Date);

        var updated = await (await client.PutAsJsonAsync(
            $"/api/incomes/{created.Id}", new IncomeRequest(6_000_000, "Freelance", Today, null))).ReadAsync<IncomeDto>();
        Assert.Equal(6_000_000, updated.Amount);
        Assert.Equal("Freelance", updated.Source);
        Assert.Null(updated.Note);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/incomes/{created.Id}")).StatusCode);
        Assert.Empty((await client.GetDataAsync()).Incomes!);

        await (await client.PostAsJsonAsync("/api/incomes/restore", updated)).ReadAsync<IncomeDto>();
        var restored = (await client.GetDataAsync()).Incomes!.Single();
        Assert.Equal(updated.Id, restored.Id);
        Assert.Equal(updated.CreatedAt, restored.CreatedAt);
    }

    [Fact]
    public async Task Expense_can_be_added_edited_deleted_and_restored()
    {
        var client = await api.NewUserAsync();

        var created = await (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(25_000, "groceries", "groceries-chicken", Today, "1 kg", "market"))).ReadAsync<ExpenseDto>();
        Assert.Equal("groceries-chicken", created.ItemId);
        Assert.Equal("1 kg", created.Quantity);

        var updated = await (await client.PutAsJsonAsync(
            $"/api/expenses/{created.Id}", new ExpenseRequest(30_000, "transport", null, Today, null, null))).ReadAsync<ExpenseDto>();
        Assert.Equal("transport", updated.CategoryId);
        Assert.Null(updated.ItemId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/expenses/{created.Id}")).StatusCode);
        Assert.Empty((await client.GetDataAsync()).Expenses!);

        await (await client.PostAsJsonAsync("/api/expenses/restore", updated)).ReadAsync<ExpenseDto>();
        var restored = (await client.GetDataAsync()).Expenses!.Single();
        Assert.Equal(updated.Id, restored.Id);
        Assert.Equal(updated.CreatedAt, restored.CreatedAt);
    }

    [Fact]
    public async Task Restoring_twice_does_not_duplicate()
    {
        var client = await api.NewUserAsync();
        var created = await (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(25_000, "groceries", null, Today, null, null))).ReadAsync<ExpenseDto>();

        await client.PostAsJsonAsync("/api/expenses/restore", created);
        await client.PostAsJsonAsync("/api/expenses/restore", created);

        Assert.Single((await client.GetDataAsync()).Expenses!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(Limits.MaxAmount + 1)]
    public async Task Bad_amounts_are_rejected(long amount)
    {
        var client = await api.NewUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(amount, "groceries", null, Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/incomes", new IncomeRequest(amount, "Salary", Today, null))).StatusCode);
    }

    [Fact]
    public async Task Floating_point_amounts_are_rejected()
    {
        var client = await api.NewUserAsync();

        var response = await client.PostAsJsonAsync(
            "/api/expenses", new { amount = 10.5, categoryId = "groceries", date = "2026-11-05" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetDataAsync()).Expenses!);
    }

    [Theory]
    [InlineData("11/05/2026")]
    [InlineData("2026-13-01")]
    [InlineData("2027-02-29")] // not a leap year
    public async Task Invalid_dates_are_rejected(string date)
    {
        var client = await api.NewUserAsync();
        var response = await client.PostAsJsonAsync("/api/expenses", new { amount = 100, categoryId = "groceries", date });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Feb_29_is_accepted_in_a_leap_year_and_a_missing_date_is_not()
    {
        var client = await api.NewUserAsync();

        var leap = await (await client.PostAsJsonAsync(
            "/api/expenses", new { amount = 100, categoryId = "groceries", date = "2028-02-29" })).ReadAsync<ExpenseDto>();
        Assert.Equal(new DateOnly(2028, 2, 29), leap.Date);

        var missing = await client.PostAsJsonAsync("/api/expenses", new { amount = 100, categoryId = "groceries" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Expenses_need_a_real_category_and_a_matching_item()
    {
        var client = await api.NewUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(100, null, null, Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(100, "missing", null, Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(100, "transport", "groceries-chicken", Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(100, "groceries", "groceries-missing", Today, null, null))).StatusCode);
    }

    [Fact]
    public async Task Missing_entries_return_404()
    {
        var client = await api.NewUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/expenses/missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            "/api/incomes/missing", new IncomeRequest(100, "Salary", Today, null))).StatusCode);
    }

    [Fact]
    public async Task Optional_fields_are_left_out_of_the_json_when_empty()
    {
        var client = await api.NewUserAsync();
        await client.PostAsJsonAsync("/api/expenses", new ExpenseRequest(100, "groceries", null, Today, null, null));

        var json = await client.GetStringAsync("/api/data");

        Assert.Contains("\"date\":\"2026-11-05\"", json);
        Assert.DoesNotContain("\"itemId\"", json);
        Assert.DoesNotContain("\"note\"", json);
    }

    [Fact]
    public async Task Acceptance_scenario_amounts_add_up()
    {
        var client = await api.NewUserAsync();
        await client.PostAsJsonAsync("/api/incomes", new IncomeRequest(5_000_000, "Salary", new DateOnly(2026, 11, 1), null));
        var macaroni = await (await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(25_000, "groceries", "groceries-macaroni-pasta", Today, null, null))).ReadAsync<ExpenseDto>();
        await client.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(60_000, "groceries", "groceries-chicken", Today, null, null));

        var data = await client.GetDataAsync();
        Assert.Equal(4_915_000, data.Incomes!.Sum(i => i.Amount) - data.Expenses!.Sum(e => e.Amount));

        await client.DeleteAsync($"/api/expenses/{macaroni.Id}");
        data = await client.GetDataAsync();
        Assert.Equal(4_940_000, data.Incomes!.Sum(i => i.Amount) - data.Expenses!.Sum(e => e.Amount));

        await client.PostAsJsonAsync("/api/expenses/restore", macaroni);
        data = await client.GetDataAsync();
        Assert.Equal(4_915_000, data.Incomes!.Sum(i => i.Amount) - data.Expenses!.Sum(e => e.Amount));
    }
}
