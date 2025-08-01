using Discord;
using Discord.WebSocket;
using System.Net.Http;
using System.Text.Json;

public class FiveMChannelUpdater
{
    private readonly DiscordSocketClient _client;
    private readonly string _fivemUrl;
    private readonly ulong _guildId;
    private readonly ulong _channelId;
    private readonly Func<LogMessage, Task>? _logFunc;
    private Timer? _timer;

    public FiveMChannelUpdater(
        DiscordSocketClient client,
        string fivemUrl,
        ulong guildId,
        ulong channelId,
        Func<LogMessage, Task>? logFunc = null)
    {
        _client = client;
        _fivemUrl = fivemUrl;
        _guildId = guildId;
        _channelId = channelId;
        _logFunc = logFunc;
    }

    public void Start()
    {
        _logFunc?.Invoke(new LogMessage(LogSeverity.Info, "FiveM", "⏱️ FiveMChannelUpdater started"));

        _timer = new Timer(async _ =>
        {
            try
            {
                using var httpClient = new HttpClient();
                string url = $"{_fivemUrl.TrimEnd('/')}/players.json";

                var response = await httpClient.GetStringAsync(url);

                if (string.IsNullOrWhiteSpace(response))
                {
                    await Log(LogSeverity.Warning, $"⚠️ Empty response from {url}");
                    return;
                }

                await Log(LogSeverity.Debug, $"📥 Raw FiveM response: {response}");

                List<JsonElement>? players;
                try
                {
                    players = JsonSerializer.Deserialize<List<JsonElement>>(response);
                }
                catch (JsonException je)
                {
                    await Log(LogSeverity.Error, $"❌ Failed to parse FiveM JSON: {je.Message}");
                    return;
                }

                int playerCount = players?.Count ?? 0;

                var guild = _client.GetGuild(_guildId);
                if (guild == null)
                {
                    await Log(LogSeverity.Warning, $"Guild not found: {_guildId}");
                    return;
                }

                var channel = guild.GetVoiceChannel(_channelId);
                if (channel == null)
                {
                    await Log(LogSeverity.Warning, $"Voice channel not found: {_channelId}");
                    return;
                }

                string newName = $"🎮┃Online Players: {playerCount}";

                if (channel.Name != newName)
                {
                    await channel.ModifyAsync(props => props.Name = newName);
                    await Log(LogSeverity.Info, $"🔄 Channel name updated to: {newName}");
                }
                else
                {
                    await Log(LogSeverity.Debug, $"⏸️ Channel name already up to date: {newName}");
                }
            }
            catch (Exception ex)
            {
                await Log(LogSeverity.Error, $"❌ Error updating FiveM channel: {ex.Message}");
            }

        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private Task Log(LogSeverity severity, string message)
    {
        Console.WriteLine(message);
        return _logFunc?.Invoke(new LogMessage(severity, "FiveM", message)) ?? Task.CompletedTask;
    }
}
