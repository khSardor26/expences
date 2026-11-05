using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyExpenses.Api.Data;
using MyExpenses.Api.Security;

namespace MyExpenses.Api.Tests;

[Collection("api")]
public class TelegramTests(ApiFactory api)
{
    private static long _nextChatId = 1000;

    private record Account(HttpClient Client, string Email, long ChatId, List<string> RecoveryCodes);

    private async Task SendToBotAsync(long chatId, string text, string chatType = "private")
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TelegramLinkService>()
            .HandleUpdateAsync(new TelegramUpdate(1, chatId, chatType, text));
    }

    private static async Task<string> GetStartTextAsync(HttpClient client)
    {
        var link = await (await client.PostAsync("/api/security/telegram/connect", null)).ReadAsync<ConnectTelegramResponse>();
        Assert.StartsWith("https://t.me/test_bot?start=", link.Url);
        return "/start " + link.Url.Split("start=")[1];
    }

    /// <summary>A new account with Telegram fully turned on, signed in.</summary>
    private async Task<Account> AccountWithTelegramAsync()
    {
        var email = ApiFactory.NewEmail();
        var client = await api.NewUserAsync(email);
        var chatId = Interlocked.Increment(ref _nextChatId);

        await SendToBotAsync(chatId, await GetStartTextAsync(client));
        var confirmed = await (await client.PostAsJsonAsync(
            "/api/security/telegram/confirm", new CodeRequest(api.Telegram.LastCode(chatId)))).ReadAsync<RecoveryCodesDto>();

        return new Account(client, email, chatId, confirmed.RecoveryCodes);
    }

    private async Task<(HttpClient Client, LoginResponse Login)> StartLoginAsync(string email, string password = ApiFactory.TestPassword)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return (client, await response.ReadAsync<LoginResponse>());
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string? challengeId, string? code = null, string? recovery = null) =>
        client.PostAsJsonAsync("/api/auth/login/verify", new VerifyLoginRequest(challengeId, code, recovery));

    /// <summary>Moves time forward by editing the code's stored times.</summary>
    private async Task AgeChallengeAsync(string id, TimeSpan age)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SecurityChallenges.Where(c => c.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(c => c.ExpiresAt, c => c.ExpiresAt - age)
            .SetProperty(c => c.SentAt, c => c.SentAt - age));
    }

    private static Task<SecurityStatusDto?> StatusAsync(HttpClient client) =>
        client.GetFromJsonAsync<SecurityStatusDto>("/api/security");

    [Fact]
    public async Task Without_Telegram_signing_in_is_unchanged()
    {
        var email = ApiFactory.NewEmail();
        await api.NewUserAsync(email);

        var (client, login) = await StartLoginAsync(email);

        Assert.False(login.RequiresCode);
        Assert.Equal(email, login.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/data")).StatusCode);
    }

    [Fact]
    public async Task Connecting_goes_off_then_linked_then_on_with_ten_recovery_codes()
    {
        var client = await api.NewUserAsync();
        Assert.Equal("off", (await StatusAsync(client))!.Telegram);
        var chatId = Interlocked.Increment(ref _nextChatId);

        await SendToBotAsync(chatId, await GetStartTextAsync(client));
        Assert.Equal("linked", (await StatusAsync(client))!.Telegram);

        var wrong = await client.PostAsJsonAsync("/api/security/telegram/confirm", new CodeRequest("000000"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("linked", (await StatusAsync(client))!.Telegram);

        var codes = await (await client.PostAsJsonAsync(
            "/api/security/telegram/confirm", new CodeRequest(api.Telegram.LastCode(chatId)))).ReadAsync<RecoveryCodesDto>();

        Assert.Equal(10, codes.RecoveryCodes.Count);
        Assert.Equal(10, codes.RecoveryCodes.Distinct().Count());
        var status = await StatusAsync(client);
        Assert.Equal("on", status!.Telegram);
        Assert.Equal(10, status.RecoveryCodesLeft);
    }

    [Fact]
    public async Task With_Telegram_on_a_password_alone_never_starts_a_session()
    {
        var account = await AccountWithTelegramAsync();

        var (client, login) = await StartLoginAsync(account.Email);

        Assert.True(login.RequiresCode);
        Assert.True(login.Delivered);
        Assert.Null(login.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/data")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_sends_no_code()
    {
        var account = await AccountWithTelegramAsync();
        var before = api.Telegram.MessageCount(account.ChatId);

        var response = await api.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email = account.Email, password = "not the password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, api.Telegram.MessageCount(account.ChatId));
    }

    [Fact]
    public async Task The_right_code_signs_in_and_works_only_once()
    {
        var account = await AccountWithTelegramAsync();
        var (client, login) = await StartLoginAsync(account.Email);
        var code = api.Telegram.LastCode(account.ChatId);

        var user = await (await VerifyAsync(client, login.ChallengeId, code)).ReadAsync<UserDto>();
        Assert.Equal(account.Email, user.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/data")).StatusCode);

        var again = await VerifyAsync(api.CreateClient(), login.ChallengeId, code);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Codes_are_only_stored_as_hashes()
    {
        var account = await AccountWithTelegramAsync();
        var (_, login) = await StartLoginAsync(account.Email);
        var code = api.Telegram.LastCode(account.ChatId);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = await db.SecurityChallenges.SingleAsync(c => c.Id == login.ChallengeId);
        Assert.DoesNotContain(code, challenge.CodeHash);
        var stored = await db.RecoveryCodes.Select(r => r.CodeHash).ToListAsync();
        Assert.DoesNotContain(account.RecoveryCodes[0], stored);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var account = await AccountWithTelegramAsync();
        var (client, login) = await StartLoginAsync(account.Email);
        var code = api.Telegram.LastCode(account.ChatId);

        await AgeChallengeAsync(login.ChallengeId!, TimeSpan.FromMinutes(6));

        var response = await VerifyAsync(client, login.ChallengeId, code);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/data")).StatusCode);
    }

    [Fact]
    public async Task Another_accounts_code_or_an_unknown_login_is_refused()
    {
        var account = await AccountWithTelegramAsync();
        var (client, login) = await StartLoginAsync(account.Email);
        var other = await AccountWithTelegramAsync();
        await StartLoginAsync(other.Email);
        var otherCode = api.Telegram.LastCode(other.ChatId);

        if (otherCode != api.Telegram.LastCode(account.ChatId))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, login.ChallengeId, otherCode)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, "not-a-real-id", "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, null, "123456")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_codes_end_the_attempt_and_lock_the_account()
    {
        var account = await AccountWithTelegramAsync();
        var (client, login) = await StartLoginAsync(account.Email);
        var right = api.Telegram.LastCode(account.ChatId);

        HttpResponseMessage last = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await VerifyAsync(client, login.ChallengeId, right == "000000" ? "000001" : "000000");
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);

        // The right code no longer helps, and neither does starting again with the password.
        Assert.NotEqual(HttpStatusCode.OK, (await VerifyAsync(client, login.ChallengeId, right)).StatusCode);
        var again = await api.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email = account.Email, password = ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
    }

    [Fact]
    public async Task Resending_waits_60_seconds_and_replaces_the_old_code_but_keeps_counting_attempts()
    {
        var account = await AccountWithTelegramAsync();
        var (client, login) = await StartLoginAsync(account.Email);
        var first = api.Telegram.LastCode(account.ChatId);

        var tooSoon = await client.PostAsJsonAsync("/api/auth/login/resend", new ChallengeRequest(login.ChallengeId));
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);

        for (var attempt = 0; attempt < 4; attempt++) await VerifyAsync(client, login.ChallengeId, "abcdef");

        await AgeChallengeAsync(login.ChallengeId!, TimeSpan.FromSeconds(61));
        var resent = await (await client.PostAsJsonAsync("/api/auth/login/resend", new ChallengeRequest(login.ChallengeId))).ReadAsync<ResendResponse>();
        Assert.True(resent.Delivered);

        var second = api.Telegram.LastCode(account.ChatId);
        if (first != second)
        {
            // A fifth wrong try ends it, even though a new code was sent.
            var fifth = await VerifyAsync(client, login.ChallengeId, first);
            Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        }
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_ignores_case_and_dashes()
    {
        var account = await AccountWithTelegramAsync();
        var code = account.RecoveryCodes[0];

        var (client, login) = await StartLoginAsync(account.Email);
        var typed = code.Replace("-", "").ToLowerInvariant();
        (await VerifyAsync(client, login.ChallengeId, recovery: typed)).EnsureSuccessStatusCode();
        Assert.Equal(9, (await StatusAsync(client))!.RecoveryCodesLeft);

        var (other, secondLogin) = await StartLoginAsync(account.Email);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(other, secondLogin.ChallengeId, recovery: code)).StatusCode);
    }

    [Fact]
    public async Task When_Telegram_is_down_signing_in_still_needs_a_code_and_recovery_codes_work()
    {
        var account = await AccountWithTelegramAsync();
        api.Telegram.FailToSend = true;
        try
        {
            var (client, login) = await StartLoginAsync(account.Email);

            Assert.True(login.RequiresCode);
            Assert.False(login.Delivered);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/data")).StatusCode);

            (await VerifyAsync(client, login.ChallengeId, recovery: account.RecoveryCodes[1])).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/data")).StatusCode);
        }
        finally
        {
            api.Telegram.FailToSend = false;
        }
    }

    [Fact]
    public async Task Turning_off_needs_the_password_and_a_code()
    {
        var account = await AccountWithTelegramAsync();
        var client = account.Client;

        var noCodeYet = await client.PostAsJsonAsync(
            "/api/security/telegram/disable", new DisableTelegramRequest(ApiFactory.TestPassword, "123456", null));
        Assert.Equal(HttpStatusCode.BadRequest, noCodeYet.StatusCode);

        (await client.PostAsync("/api/security/telegram/disable/code", null)).EnsureSuccessStatusCode();
        var code = api.Telegram.LastCode(account.ChatId);

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/security/telegram/disable", new DisableTelegramRequest("not the password", code, null));
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Equal("on", (await StatusAsync(client))!.Telegram);

        var ok = await client.PostAsJsonAsync(
            "/api/security/telegram/disable", new DisableTelegramRequest(ApiFactory.TestPassword, code, null));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var status = await StatusAsync(client);
        Assert.Equal("off", status!.Telegram);
        Assert.Equal(0, status.RecoveryCodesLeft);
        Assert.False((await StartLoginAsync(account.Email)).Login.RequiresCode);
    }

    [Fact]
    public async Task Turning_off_works_with_a_recovery_code_when_Telegram_is_lost()
    {
        var account = await AccountWithTelegramAsync();

        var ok = await account.Client.PostAsJsonAsync(
            "/api/security/telegram/disable",
            new DisableTelegramRequest(ApiFactory.TestPassword, null, account.RecoveryCodes[2]));

        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("off", (await StatusAsync(account.Client))!.Telegram);
    }

    [Fact]
    public async Task One_account_cant_use_or_affect_another_accounts_Telegram()
    {
        var alice = await AccountWithTelegramAsync();
        var bob = await api.NewUserAsync();

        // Bob isn't connected, so Alice's code means nothing for him.
        var confirm = await bob.PostAsJsonAsync("/api/security/telegram/confirm", new CodeRequest(api.Telegram.LastCode(alice.ChatId)));
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal("off", (await StatusAsync(bob))!.Telegram);

        // Bob can't use Alice's recovery code to sign in to his own account.
        var bobWithTelegram = await AccountWithTelegramAsync();
        var (client, login) = await StartLoginAsync(bobWithTelegram.Email);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, login.ChallengeId, recovery: alice.RecoveryCodes[0])).StatusCode);
    }

    [Fact]
    public async Task The_bot_only_links_with_a_valid_link_in_a_private_chat()
    {
        var client = await api.NewUserAsync();
        var start = await GetStartTextAsync(client);
        var chatId = Interlocked.Increment(ref _nextChatId);

        await SendToBotAsync(chatId, start, chatType: "group");
        Assert.Equal("off", (await StatusAsync(client))!.Telegram);

        await SendToBotAsync(chatId, "/start wrong-token");
        await SendToBotAsync(chatId, "hello");
        await SendToBotAsync(chatId, "/start");
        Assert.Equal("off", (await StatusAsync(client))!.Telegram);
        Assert.Equal(3, api.Telegram.MessageCount(chatId));

        await SendToBotAsync(chatId, start);
        Assert.Equal("linked", (await StatusAsync(client))!.Telegram);

        // The link is used up: another chat can't take over with it.
        var attacker = Interlocked.Increment(ref _nextChatId);
        await SendToBotAsync(attacker, start);
        Assert.DoesNotMatch(@"\d{6}", api.Telegram.Sent.Last(sent => sent.ChatId == attacker).Text);
    }

    [Fact]
    public async Task An_old_link_is_refused()
    {
        var client = await api.NewUserAsync();
        var start = await GetStartTextAsync(client);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = SecurityCodes.Sha256(start.Split(' ')[1]);
            await db.TelegramLinks.Where(l => l.TokenHash == hash)
                .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await SendToBotAsync(Interlocked.Increment(ref _nextChatId), start);
        Assert.Equal("off", (await StatusAsync(client))!.Telegram);
    }

    [Fact]
    public async Task Cancelling_an_unconfirmed_connection_clears_it()
    {
        var client = await api.NewUserAsync();
        await SendToBotAsync(Interlocked.Increment(ref _nextChatId), await GetStartTextAsync(client));
        Assert.Equal("linked", (await StatusAsync(client))!.Telegram);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/security/telegram/cancel", null)).StatusCode);

        Assert.Equal("off", (await StatusAsync(client))!.Telegram);
    }

    [Fact]
    public async Task Connecting_says_so_clearly_when_no_bot_token_is_set()
    {
        var client = await api.NewUserAsync();
        api.Telegram.IsConfigured = false;
        try
        {
            Assert.False((await StatusAsync(client))!.TelegramAvailable);
            var response = await client.PostAsync("/api/security/telegram/connect", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            api.Telegram.IsConfigured = true;
        }
    }

    [Fact]
    public async Task Sign_out_everywhere_ends_every_session_but_not_the_account()
    {
        var email = ApiFactory.NewEmail();
        var first = await api.NewUserAsync(email);
        var (second, _) = await StartLoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/data")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/security/sign-out-everywhere", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/data")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/data")).StatusCode);

        var (third, _) = await StartLoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await third.GetAsync("/api/data")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_Telegram_data()
    {
        var account = await AccountWithTelegramAsync();
        await StartLoginAsync(account.Email);

        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = (await users.FindByEmailAsync(account.Email))!;

        Assert.True((await users.DeleteAsync(user)).Succeeded);

        Assert.False(await db.SecurityChallenges.AnyAsync(c => c.UserId == user.Id));
        Assert.False(await db.RecoveryCodes.AnyAsync(r => r.UserId == user.Id));
    }
}

public class SecurityCodesTests
{
    [Fact]
    public void Codes_have_the_expected_shapes()
    {
        Assert.Matches(@"^\d{6}$", SecurityCodes.NewCode());
        Assert.Matches(@"^[A-Za-z0-9_-]{32}$", SecurityCodes.NewLinkToken());
        Assert.Matches(@"^[A-HJ-NP-Z2-9]{5}-[A-HJ-NP-Z2-9]{5}$", SecurityCodes.NewRecoveryCode());
    }

    [Fact]
    public void Recovery_codes_normalize_the_way_people_type_them()
    {
        Assert.Equal("K7MQ2XR4TN", SecurityCodes.NormalizeRecoveryCode(" k7mq2-xr4tn "));
    }
}
