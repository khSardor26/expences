using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;
using MyExpenses.Api.Security;

namespace MyExpenses.Api.Endpoints;

/// <summary>The account's security settings. All of these need a signed-in user.</summary>
public static class SecurityEndpoints
{
    public static void MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var security = app.MapGroup("/api/security");

        security.MapGet("/", GetStatus);
        security.MapPost("/sign-out-everywhere", SignOutEverywhere);

        var telegram = security.MapGroup("/telegram");
        telegram.MapPost("/connect", Connect);
        telegram.MapPost("/confirm", Confirm).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        telegram.MapPost("/resend", ResendConfirmCode);
        telegram.MapPost("/cancel", Cancel);
        telegram.MapPost("/disable/code", SendDisableCode);
        telegram.MapPost("/disable", Disable).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
    }

    private static IResult BadRequest(string message) =>
        Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult TelegramUnavailable() => Results.Problem(
        "Couldn't reach Telegram. Try again in a moment.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> GetStatus(ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db, ITelegramClient telegram)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var state = user.TelegramEnabled ? "on" : user.TelegramChatId is null ? "off" : "linked";
        var left = await db.RecoveryCodes.CountAsync(r => r.UserId == user.Id);
        return Results.Ok(new SecurityStatusDto(telegram.IsConfigured, state, left));
    }

    /// <summary>Ends every session, including this one, by changing the value they all depend on.</summary>
    private static async Task<IResult> SignOutEverywhere(ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        await users.UpdateSecurityStampAsync(user);
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    /// <summary>Makes a one-time link to the bot. Pressing Start there connects the chat.</summary>
    private static async Task<IResult> Connect(ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db, ITelegramClient telegram)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (user.TelegramEnabled) return BadRequest("Telegram is already on.");
        if (!telegram.IsConfigured)
        {
            return Results.Problem(
                "Telegram isn't set up on this server yet.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        string username;
        try
        {
            username = await telegram.GetBotUsernameAsync();
        }
        catch (TelegramException)
        {
            return TelegramUnavailable();
        }

        var token = SecurityCodes.NewLinkToken();
        await db.TelegramLinks.Where(l => l.UserId == user.Id).ExecuteDeleteAsync();
        db.TelegramLinks.Add(new TelegramLink
        {
            UserId = user.Id,
            TokenHash = SecurityCodes.Sha256(token),
            ExpiresAt = DateTimeOffset.UtcNow + TelegramLinkService.LinkLifetime,
        });
        await db.SaveChangesAsync();

        return Results.Ok(new ConnectTelegramResponse($"https://t.me/{username}?start={token}"));
    }

    /// <summary>Turns Telegram on, now that a code has proved messages arrive. Recovery codes are only returned here.</summary>
    private static async Task<IResult> Confirm(
        CodeRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AppDbContext db,
        SecurityChallengeService challenges)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (user.TelegramEnabled || user.TelegramChatId is null) return BadRequest("Press Connect Telegram first.");

        var challenge = await FindChallengeAsync(db, user, ChallengePurpose.Enable);
        if (challenge is null) return BadRequest("That code has expired. Ask for a new one.");

        switch (await challenges.CheckAsync(challenge, user, request.Code, recoveryCode: null))
        {
            case CheckResult.Valid:
                user.TelegramEnabled = true;
                await users.UpdateAsync(user);
                return Results.Ok(new RecoveryCodesDto(await challenges.CreateRecoveryCodesAsync(user)));
            case CheckResult.Wrong:
                return BadRequest("That code isn't right. Check it and try again.");
            default:
                return BadRequest("That code has expired. Ask for a new one.");
        }
    }

    private static async Task<IResult> ResendConfirmCode(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AppDbContext db,
        SecurityChallengeService challenges)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (user.TelegramEnabled || user.TelegramChatId is null) return BadRequest("Press Connect Telegram first.");

        return await SendAnotherCodeAsync(user, ChallengePurpose.Enable, db, challenges);
    }

    /// <summary>Abandons a connection that was never confirmed.</summary>
    private static async Task<IResult> Cancel(ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (user.TelegramEnabled) return BadRequest("Telegram is already on.");

        await db.TelegramLinks.Where(l => l.UserId == user.Id).ExecuteDeleteAsync();
        await db.SecurityChallenges.Where(c => c.UserId == user.Id).ExecuteDeleteAsync();
        user.TelegramChatId = null;
        await users.UpdateAsync(user);
        return Results.NoContent();
    }

    private static async Task<IResult> SendDisableCode(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AppDbContext db,
        SecurityChallengeService challenges)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (!user.TelegramEnabled) return BadRequest("Telegram is already off.");

        return await SendAnotherCodeAsync(user, ChallengePurpose.Disable, db, challenges);
    }

    /// <summary>Turning Telegram off needs the password as well as a code or recovery code.</summary>
    private static async Task<IResult> Disable(
        DisableTelegramRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AppDbContext db,
        SecurityChallengeService challenges)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (!user.TelegramEnabled) return BadRequest("Telegram is already off.");

        if (!await users.CheckPasswordAsync(user, request.Password ?? ""))
            return BadRequest("Incorrect password.");

        var challenge = await FindChallengeAsync(db, user, ChallengePurpose.Disable);
        if (challenge is null)
        {
            if (string.IsNullOrWhiteSpace(request.RecoveryCode)) return BadRequest("Send yourself a code first.");
            // A recovery code is for when Telegram can't deliver one, so no code needs to have been sent.
            (challenge, _) = await challenges.StartAsync(user, ChallengePurpose.Disable);
        }

        switch (await challenges.CheckAsync(challenge, user, request.Code, request.RecoveryCode))
        {
            case CheckResult.Valid:
                await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
                await db.SecurityChallenges.Where(c => c.UserId == user.Id).ExecuteDeleteAsync();
                user.TelegramEnabled = false;
                user.TelegramChatId = null;
                await users.UpdateAsync(user);
                return Results.NoContent();
            case CheckResult.Wrong:
                return BadRequest("That code isn't right. Check it and try again.");
            default:
                return BadRequest("That code has expired. Send yourself a new one.");
        }
    }

    private static Task<SecurityChallenge?> FindChallengeAsync(AppDbContext db, AppUser user, string purpose) =>
        db.SecurityChallenges.SingleOrDefaultAsync(c => c.UserId == user.Id && c.Purpose == purpose);

    private static async Task<IResult> SendAnotherCodeAsync(AppUser user, string purpose, AppDbContext db, SecurityChallengeService challenges)
    {
        var existing = await FindChallengeAsync(db, user, purpose);
        if (existing is not null)
        {
            var wait = SecurityChallengeService.SecondsUntilResend(existing);
            if (wait > 0) return AuthEndpoints.ResendTooSoon(wait);
        }

        var delivered = existing is null
            ? (await challenges.StartAsync(user, purpose)).Delivered
            : await challenges.ResendAsync(existing, user);

        return delivered ? Results.Ok(new ResendResponse(true)) : TelegramUnavailable();
    }
}
