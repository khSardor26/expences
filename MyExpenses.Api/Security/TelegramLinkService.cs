using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;

namespace MyExpenses.Api.Security;

/// <summary>Handles what people send to the bot: connecting a chat to an account.</summary>
public class TelegramLinkService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITelegramClient telegram,
    SecurityChallengeService challenges)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(10);

    public async Task HandleUpdateAsync(TelegramUpdate update)
    {
        // A group chat would show sign-in codes to everyone in it.
        if (update.ChatType != "private") return;

        var text = update.Text?.Trim() ?? "";
        if (!text.StartsWith("/start", StringComparison.Ordinal))
        {
            await telegram.SendMessageAsync(update.ChatId, "This bot only sends myExpenses sign-in codes.");
            return;
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var token = parts.Length == 2 ? parts[1] : null;
        if (token is null)
        {
            await telegram.SendMessageAsync(update.ChatId, "To connect, open myExpenses, go to Settings and press Connect Telegram.");
            return;
        }

        var hash = SecurityCodes.Sha256(token);
        var link = await db.TelegramLinks.SingleOrDefaultAsync(l => l.TokenHash == hash);
        var user = link is null ? null : await users.FindByIdAsync(link.UserId);
        if (link is null || user is null || link.ExpiresAt <= DateTimeOffset.UtcNow || user.TelegramEnabled)
        {
            await telegram.SendMessageAsync(update.ChatId, "This link has expired. Press Connect Telegram in myExpenses to get a new one.");
            return;
        }

        db.TelegramLinks.Remove(link);
        user.TelegramChatId = update.ChatId;
        await db.SaveChangesAsync();

        await challenges.StartAsync(user, ChallengePurpose.Enable);
    }
}

/// <summary>Asks Telegram for new messages to the bot, for as long as the app runs.</summary>
public class TelegramUpdatePoller(
    IServiceScopeFactory scopes,
    ITelegramClient telegram,
    ILogger<TelegramUpdatePoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!telegram.IsConfigured)
        {
            logger.LogInformation("No Telegram bot token is set, so Telegram sign-in codes are unavailable.");
            return;
        }

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var update in await telegram.GetUpdatesAsync(offset, stoppingToken))
                {
                    offset = update.UpdateId + 1;
                    await HandleAsync(update);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (TelegramException exception)
            {
                logger.LogWarning("Telegram polling failed: {Reason}", exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TelegramUpdate update)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TelegramLinkService>().HandleUpdateAsync(update);
        }
        catch (TelegramException exception)
        {
            // One message that can't be answered must not stop the rest.
            logger.LogWarning("Couldn't answer a Telegram message: {Reason}", exception.Message);
        }
    }
}
