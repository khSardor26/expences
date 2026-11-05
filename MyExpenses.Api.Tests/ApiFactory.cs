using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyExpenses.Api.Security;
using Npgsql;

namespace MyExpenses.Api.Tests;

/// <summary>
/// Runs the real API against a real Postgres, in a throwaway database that is
/// created for this test run and dropped afterwards. Set MYEXPENSES_TEST_DB to
/// point at a different server.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"myexpenses_test_{Guid.NewGuid():N}";
    private readonly int _authRateLimit;

    private static string ServerConnection =>
        Environment.GetEnvironmentVariable("MYEXPENSES_TEST_DB") ?? "Host=localhost";

    public ApiFactory() : this(authRateLimit: 100_000) { }

    protected ApiFactory(int authRateLimit) => _authRateLimit = authRateLimit;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _databaseName };
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", connection.ConnectionString);
        builder.UseSetting("Auth:RateLimitPerMinute", _authRateLimit.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITelegramClient>();
            services.AddSingleton<ITelegramClient>(Telegram);
        });
    }

    public FakeTelegram Telegram { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();

        // Drops only the uniquely named database this run created.
        var admin = new NpgsqlConnectionStringBuilder(ServerConnection) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE)""";
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>A client signed in to a brand new account.</summary>
    public async Task<HttpClient> NewUserAsync(string? email = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { email = email ?? NewEmail(), password = TestPassword });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public const string TestPassword = "correct horse battery";

    public static string NewEmail() => $"{Guid.NewGuid():N}@example.com";
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;

public static class ClientExtensions
{
    public static async Task<AppDataDto> GetDataAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<AppDataDto>("/api/data"))!;

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
