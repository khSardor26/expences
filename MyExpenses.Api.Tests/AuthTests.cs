using System.Net;
using System.Net.Http.Json;

namespace MyExpenses.Api.Tests;

[Collection("api")]
public class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Register_signs_in_and_creates_the_default_data()
    {
        var email = ApiFactory.NewEmail();
        var client = await api.NewUserAsync(email);

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal(email, me!.Email);

        var data = await client.GetDataAsync();
        Assert.Equal(
            ["Groceries", "Eating out", "Transport", "Home & Bills", "Health", "Shopping", "Education", "Entertainment", "Gifts & Family", "Other"],
            data.Categories!.Select(c => c.Name));
        Assert.Equal(31, data.Items!.Count);
        Assert.Equal(11, data.Items.Count(i => i.CategoryId == "groceries"));
        Assert.Contains(data.Items, i => i.Id == "groceries-sugar-tea");
        Assert.Equal(["Salary", "Freelance", "Gift", "Other"], data.IncomeSources);
        Assert.Empty(data.Incomes!);
        Assert.Empty(data.Expenses!);

        var settings = data.Settings!;
        Assert.Equal("UZS", settings.Currency!.Code);
        Assert.Equal(0, settings.Currency.Decimals);
        Assert.Equal("month", settings.PeriodType);
        Assert.Equal(1, settings.WeekStartsOn);
        Assert.Equal(1, settings.MonthStartDay);
    }

    [Theory]
    [InlineData("/api/data")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/security")]
    public async Task Reads_need_a_session(string path)
    {
        var response = await api.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/incomes")]
    [InlineData("PUT", "/api/incomes/x")]
    [InlineData("DELETE", "/api/incomes/x")]
    [InlineData("POST", "/api/incomes/restore")]
    [InlineData("POST", "/api/expenses")]
    [InlineData("PUT", "/api/expenses/x")]
    [InlineData("DELETE", "/api/expenses/x")]
    [InlineData("POST", "/api/expenses/restore")]
    [InlineData("POST", "/api/categories")]
    [InlineData("PUT", "/api/categories/order")]
    [InlineData("PUT", "/api/categories/x")]
    [InlineData("POST", "/api/categories/x/archive")]
    [InlineData("POST", "/api/items")]
    [InlineData("PUT", "/api/items/order")]
    [InlineData("PUT", "/api/items/x")]
    [InlineData("POST", "/api/items/x/archive")]
    [InlineData("PUT", "/api/settings")]
    [InlineData("PUT", "/api/income-sources")]
    [InlineData("POST", "/api/data/import")]
    [InlineData("DELETE", "/api/data")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/security/sign-out-everywhere")]
    [InlineData("POST", "/api/security/telegram/connect")]
    [InlineData("POST", "/api/security/telegram/confirm")]
    [InlineData("POST", "/api/security/telegram/resend")]
    [InlineData("POST", "/api/security/telegram/cancel")]
    [InlineData("POST", "/api/security/telegram/disable/code")]
    [InlineData("POST", "/api/security/telegram/disable")]
    public async Task Every_write_needs_a_session(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        var response = await api.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_and_logout_work()
    {
        var email = ApiFactory.NewEmail();
        await api.NewUserAsync(email);

        var client = api.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/data")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/data")).StatusCode);
    }

    [Fact]
    public async Task Session_cookie_cannot_be_read_by_scripts_or_sent_by_other_sites()
    {
        var response = await api.CreateClient().PostAsJsonAsync(
            "/api/auth/register", new { email = ApiFactory.NewEmail(), password = ApiFactory.TestPassword });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("myexpenses.session"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var email = ApiFactory.NewEmail();
        await api.NewUserAsync(email);
        var client = api.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "not the password" });
        var unknownEmail = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = ApiFactory.NewEmail(), password = "not the password" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var wrongPasswordProblem = await wrongPassword.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        var unknownEmailProblem = await unknownEmail.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(wrongPasswordProblem!.Detail, unknownEmailProblem!.Detail);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var email = ApiFactory.NewEmail();
        await api.NewUserAsync(email);
        var client = api.CreateClient();

        HttpResponseMessage last = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "not the password" });
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);

        var correct = await client.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", ApiFactory.TestPassword)]
    [InlineData("user@example.com", "short")]
    [InlineData("", ApiFactory.TestPassword)]
    [InlineData("user@example.com", "")]
    public async Task Register_rejects_bad_credentials(string email, string password)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_an_email_already_in_use_and_keeps_the_original_account()
    {
        var email = ApiFactory.NewEmail();
        var owner = await api.NewUserAsync(email);

        var second = await api.CreateClient().PostAsJsonAsync(
            "/api/auth/register", new { email, password = "a different password" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        var login = await api.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(10, (await owner.GetDataAsync()).Categories!.Count);
    }
}

public class RateLimitedApiFactory() : ApiFactory(authRateLimit: 3);

public class RateLimitTests(RateLimitedApiFactory api) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Sign_in_attempts_are_rate_limited()
    {
        var client = api.CreateClient();
        var body = new { email = "nobody@example.com", password = "whatever-it-is" };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var allowed = await client.PostAsJsonAsync("/api/auth/login", body);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }
}
