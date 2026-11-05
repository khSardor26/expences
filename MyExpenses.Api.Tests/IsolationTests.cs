using System.Net;
using System.Net.Http.Json;

namespace MyExpenses.Api.Tests;

/// <summary>One user must never be able to see or change another user's data.</summary>
[Collection("api")]
public class IsolationTests(ApiFactory api)
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public async Task Users_only_see_their_own_data()
    {
        var alice = await api.NewUserAsync();
        var bob = await api.NewUserAsync();

        await alice.PostAsJsonAsync("/api/incomes", new IncomeRequest(5_000_000, "Salary", Today, null));
        await alice.PostAsJsonAsync("/api/expenses", new ExpenseRequest(25_000, "groceries", null, Today, null, "private"));

        var bobsData = await bob.GetDataAsync();
        Assert.Empty(bobsData.Incomes!);
        Assert.Empty(bobsData.Expenses!);
    }

    [Fact]
    public async Task Users_cannot_edit_or_delete_each_others_entries()
    {
        var alice = await api.NewUserAsync();
        var bob = await api.NewUserAsync();

        var income = await (await alice.PostAsJsonAsync(
            "/api/incomes", new IncomeRequest(5_000_000, "Salary", Today, null))).ReadAsync<IncomeDto>();
        var expense = await (await alice.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(25_000, "groceries", null, Today, null, null))).ReadAsync<ExpenseDto>();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(
            $"/api/incomes/{income.Id}", new IncomeRequest(1, "Stolen", Today, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/incomes/{income.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(
            $"/api/expenses/{expense.Id}", new ExpenseRequest(1, "groceries", null, Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/expenses/{expense.Id}")).StatusCode);

        var data = await alice.GetDataAsync();
        Assert.Equal(5_000_000, data.Incomes!.Single().Amount);
        Assert.Equal(25_000, data.Expenses!.Single().Amount);
    }

    [Fact]
    public async Task Restoring_with_the_same_id_does_not_touch_the_other_users_entry()
    {
        var alice = await api.NewUserAsync();
        var bob = await api.NewUserAsync();

        var expense = await (await alice.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(25_000, "groceries", null, Today, null, null))).ReadAsync<ExpenseDto>();

        var bobsCopy = expense with { Amount = 1 };
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsJsonAsync("/api/expenses/restore", bobsCopy)).StatusCode);

        Assert.Equal(25_000, (await alice.GetDataAsync()).Expenses!.Single().Amount);
        Assert.Equal(1, (await bob.GetDataAsync()).Expenses!.Single().Amount);
    }

    [Fact]
    public async Task Users_cannot_use_each_others_custom_categories_or_items()
    {
        var alice = await api.NewUserAsync();
        var bob = await api.NewUserAsync();

        var category = await (await alice.PostAsJsonAsync(
            "/api/categories", new CategoryRequest("Alice only", "🔒", "#000000"))).ReadAsync<CategoryDto>();
        var item = await (await alice.PostAsJsonAsync(
            "/api/items", new ItemRequest(category.Id, "Secret"))).ReadAsync<ItemDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(1_000, category.Id, null, Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync(
            "/api/expenses", new ExpenseRequest(1_000, "groceries", item.Id, Today, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(
            $"/api/categories/{category.Id}", new CategoryRequest("Hijacked", "x", "#000000"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync(
            $"/api/categories/{category.Id}/archive", new ArchiveCategoryRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(
            $"/api/items/{item.Id}", new ItemRequest(null, "Hijacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/items/{item.Id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(
            "/api/categories/order", new ReorderRequest([category.Id]))).StatusCode);
    }

    [Fact]
    public async Task Reset_only_affects_the_user_who_asked()
    {
        var alice = await api.NewUserAsync();
        var bob = await api.NewUserAsync();
        await alice.PostAsJsonAsync("/api/expenses", new ExpenseRequest(25_000, "groceries", null, Today, null, null));
        await bob.PostAsJsonAsync("/api/expenses", new ExpenseRequest(60_000, "groceries", null, Today, null, null));

        (await bob.DeleteAsync("/api/data")).EnsureSuccessStatusCode();

        Assert.Single((await alice.GetDataAsync()).Expenses!);
        Assert.Empty((await bob.GetDataAsync()).Expenses!);
    }
}
