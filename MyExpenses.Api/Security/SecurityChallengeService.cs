using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Security;

public static class ChallengePurpose
{
    public const string SignIn = "signin";
    public const string Enable = "enable";
    public const string Disable = "disable";
}

public enum CheckResult
{
    Valid,
    Wrong,
    TooManyAttempts,
    Expired,
}

/// <summary>Sends one-time codes to Telegram and checks them.</summary>
public class SecurityChallengeService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITelegramClient telegram,
    ILogger<SecurityChallengeService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResendWait = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan LockoutAfterTooManyCodes = TimeSpan.FromMinutes(15);
    public const int MaxAttempts = 5;
    public const int RecoveryCodeCount = 10;

    /// <summary>Replaces any earlier code for this purpose and sends a new one.</summary>
    public async Task<(SecurityChallenge Challenge, bool Delivered)> StartAsync(AppUser user, string purpose)
    {
        await db.SecurityChallenges
            .Where(c => c.UserId == user.Id && c.Purpose == purpose)
            .ExecuteDeleteAsync();

        var code = SecurityCodes.NewCode();
        var now = DateTimeOffset.UtcNow;
        var challenge = new SecurityChallenge
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user.Id,
            Purpose = purpose,
            CodeHash = users.PasswordHasher.HashPassword(user, code),
            ExpiresAt = now + CodeLifetime,
            SentAt = now,
        };
        db.SecurityChallenges.Add(challenge);
        await db.SaveChangesAsync();

        return (challenge, await SendAsync(user, purpose, code));
    }

    /// <summary>A fresh code for the same challenge. Wrong attempts so far still count.</summary>
    public async Task<bool> ResendAsync(SecurityChallenge challenge, AppUser user)
    {
        var code = SecurityCodes.NewCode();
        var now = DateTimeOffset.UtcNow;
        challenge.CodeHash = users.PasswordHasher.HashPassword(user, code);
        challenge.ExpiresAt = now + CodeLifetime;
        challenge.SentAt = now;
        await db.SaveChangesAsync();
        return await SendAsync(user, challenge.Purpose, code);
    }

    /// <summary>Seconds until another code may be sent. 0 means now.</summary>
    public static int SecondsUntilResend(SecurityChallenge challenge)
    {
        var remaining = challenge.SentAt + ResendWait - DateTimeOffset.UtcNow;
        return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
    }

    /// <summary>Checks a Telegram code, or a recovery code if given. A code works only once.</summary>
    public async Task<CheckResult> CheckAsync(SecurityChallenge challenge, AppUser user, string? code, string? recoveryCode)
    {
        if (challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            db.SecurityChallenges.Remove(challenge);
            await db.SaveChangesAsync();
            return CheckResult.Expired;
        }

        var valid = !string.IsNullOrWhiteSpace(recoveryCode)
            ? await RedeemRecoveryCodeAsync(user, recoveryCode)
            : IsCorrect(challenge, user, code);

        if (valid)
        {
            db.SecurityChallenges.Remove(challenge);
            await db.SaveChangesAsync();
            return CheckResult.Valid;
        }

        challenge.Attempts++;
        if (challenge.Attempts < MaxAttempts)
        {
            await db.SaveChangesAsync();
            return CheckResult.Wrong;
        }

        db.SecurityChallenges.Remove(challenge);
        await db.SaveChangesAsync();
        return CheckResult.TooManyAttempts;
    }

    /// <summary>Makes a new set, replacing any old ones. The plain codes exist only in this list.</summary>
    public async Task<List<string>> CreateRecoveryCodesAsync(AppUser user)
    {
        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();

        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => SecurityCodes.NewRecoveryCode()).ToList();
        db.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCode
        {
            UserId = user.Id,
            CodeHash = SecurityCodes.Sha256(SecurityCodes.NormalizeRecoveryCode(code)),
        }));
        await db.SaveChangesAsync();
        return codes;
    }

    private bool IsCorrect(SecurityChallenge challenge, AppUser user, string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        users.PasswordHasher.VerifyHashedPassword(user, challenge.CodeHash, code.Trim()) != PasswordVerificationResult.Failed;

    private async Task<bool> RedeemRecoveryCodeAsync(AppUser user, string recoveryCode)
    {
        var hash = SecurityCodes.Sha256(SecurityCodes.NormalizeRecoveryCode(recoveryCode));
        return await db.RecoveryCodes.Where(r => r.UserId == user.Id && r.CodeHash == hash).ExecuteDeleteAsync() > 0;
    }

    private async Task<bool> SendAsync(AppUser user, string purpose, string code)
    {
        if (user.TelegramChatId is not { } chatId) return false;

        try
        {
            await telegram.SendMessageAsync(chatId, MessageFor(purpose, code));
            return true;
        }
        catch (TelegramException exception)
        {
            logger.LogWarning("Couldn't send a {Purpose} code: {Reason}", purpose, exception.Message);
            return false;
        }
    }

    private static string MessageFor(string purpose, string code) => purpose switch
    {
        ChallengePurpose.SignIn =>
            $"myExpenses sign-in code: {code}\nIt expires in 5 minutes.\n\nIf this wasn't you, someone has your password.",
        ChallengePurpose.Enable =>
            $"myExpenses code to finish connecting Telegram: {code}\nIt expires in 5 minutes.",
        _ =>
            $"myExpenses code to turn off Telegram sign-in: {code}\nIt expires in 5 minutes.\n\nIf this wasn't you, someone is signed in to your account.",
    };
}
