using System.Text.Json.Serialization;

namespace MyExpenses.Api.Security;

public record TelegramUpdate(long UpdateId, long ChatId, string ChatType, string? Text);

public class TelegramException(string message) : Exception(message);

/// <summary>The few things this app needs from Telegram. Tests replace it with a fake.</summary>
public interface ITelegramClient
{
    /// <summary>False until a bot token has been set up.</summary>
    bool IsConfigured { get; }

    Task<string> GetBotUsernameAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken);
}

/// <summary>
/// Talks to the Telegram Bot API. The bot token is part of every request URL, so this client
/// has HTTP logging removed (see Program.cs) and its errors never include the URL.
/// </summary>
public class TelegramClient(HttpClient http, IConfiguration configuration) : ITelegramClient
{
    private const int LongPollSeconds = 25;

    private string? Token => configuration["Telegram:BotToken"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);

    public async Task<string> GetBotUsernameAsync(CancellationToken cancellationToken = default)
    {
        var me = await CallAsync<BotUser>("getMe", new { }, cancellationToken);
        return me.Username ?? throw new TelegramException("Telegram returned a bot without a username.");
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        await CallAsync<object>("sendMessage", new { chat_id = chatId, text }, cancellationToken);
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var request = new { offset, timeout = LongPollSeconds, allowed_updates = new[] { "message" } };
        var updates = await CallAsync<List<RawUpdate>>("getUpdates", request, cancellationToken);
        return updates
            .Where(update => update.Message?.Chat is not null)
            .Select(update => new TelegramUpdate(
                update.UpdateId, update.Message!.Chat!.Id, update.Message.Chat.Type ?? "", update.Message.Text))
            .ToList();
    }

    private async Task<T> CallAsync<T>(string method, object body, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new TelegramException("The Telegram bot token isn't set.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(LongPollSeconds + 10));

        try
        {
            using var response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{Token}/{method}", body, timeout.Token);
            var result = await response.Content.ReadFromJsonAsync<RawResponse<T>>(timeout.Token);
            if (result is not { Ok: true, Result: not null })
                throw new TelegramException($"Telegram refused {method} ({(int)response.StatusCode}).");
            return result.Result;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A new exception on purpose: the original could mention the URL, which holds the token.
            throw new TelegramException($"Couldn't reach Telegram for {method}.");
        }
    }

    private record RawResponse<T>([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("result")] T? Result);
    private record BotUser([property: JsonPropertyName("username")] string? Username);
    private record RawChat([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("type")] string? Type);
    private record RawMessage([property: JsonPropertyName("chat")] RawChat? Chat, [property: JsonPropertyName("text")] string? Text);
    private record RawUpdate([property: JsonPropertyName("update_id")] long UpdateId, [property: JsonPropertyName("message")] RawMessage? Message);
}
