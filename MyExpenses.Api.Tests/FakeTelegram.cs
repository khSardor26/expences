using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MyExpenses.Api.Security;

namespace MyExpenses.Api.Tests;

/// <summary>Stands in for Telegram: nothing leaves the machine, and tests can read what was "sent".</summary>
public partial class FakeTelegram : ITelegramClient
{
    public ConcurrentQueue<(long ChatId, string Text)> Sent { get; } = new();
    public bool IsConfigured { get; set; } = true;
    public bool FailToSend { get; set; }

    public Task<string> GetBotUsernameAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("test_bot");

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        if (FailToSend) throw new TelegramException("Couldn't reach Telegram for sendMessage.");
        Sent.Enqueue((chatId, text));
        return Task.CompletedTask;
    }

    /// <summary>Waits until shutdown. Tests hand updates straight to TelegramLinkService instead.</summary>
    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return [];
    }

    public string LastCode(long chatId)
    {
        var message = Sent.Last(sent => sent.ChatId == chatId).Text;
        return SixDigits().Match(message).Value;
    }

    public int MessageCount(long chatId) => Sent.Count(sent => sent.ChatId == chatId);

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
