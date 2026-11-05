using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Tests;

/// <summary>Rules the database itself enforces, whatever the application code does.</summary>
[Collection("api")]
public class DataIntegrityTests(ApiFactory api)
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public async Task Deleting_an_account_removes_all_of_its_data_and_nobody_elses()
    {
        var email = ApiFactory.NewEmail();
        var leaving = await api.NewUserAsync(email);
        var staying = await api.NewUserAsync();
        foreach (var client in new[] { leaving, staying })
        {
            await client.PostAsJsonAsync("/api/incomes", new IncomeRequest(100, "Salary", Today, null));
            await client.PostAsJsonAsync("/api/expenses", new ExpenseRequest(100, "groceries", "groceries-chicken", Today, null, null));
        }

        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = (await users.FindByEmailAsync(email))!;

        Assert.True((await users.DeleteAsync(user)).Succeeded);

        Assert.False(await db.Expenses.AnyAsync(e => e.UserId == user.Id));
        Assert.False(await db.Incomes.AnyAsync(i => i.UserId == user.Id));
        Assert.False(await db.Items.AnyAsync(i => i.UserId == user.Id));
        Assert.False(await db.Categories.AnyAsync(c => c.UserId == user.Id));
        Assert.False(await db.UserSettings.AnyAsync(s => s.UserId == user.Id));

        var remaining = await staying.GetDataAsync();
        Assert.Single(remaining.Expenses!);
        Assert.Single(remaining.Incomes!);
    }

    [Fact]
    public async Task The_database_refuses_to_delete_a_category_or_item_still_used_by_an_expense()
    {
        var email = ApiFactory.NewEmail();
        var client = await api.NewUserAsync(email);
        await client.PostAsJsonAsync("/api/expenses", new ExpenseRequest(100, "groceries", "groceries-chicken", Today, null, null));

        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = (await users.FindByEmailAsync(email))!.Id;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Items.Where(i => i.UserId == userId && i.Id == "groceries-chicken").ExecuteDeleteAsync());
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Categories.Where(c => c.UserId == userId && c.Id == "groceries").ExecuteDeleteAsync());

        Assert.Single((await client.GetDataAsync()).Expenses!);
    }
}
