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
    private static readonly HttpClient _httpClient = new();

    private int _lastPlayerCount = -1;
    private int _failCount = 0;
    private const int MaxFailsBeforePause = 10;
    private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PauseInterval = TimeSpan.FromHours(1);

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

        _timer = new Timer(async _ => await UpdateAsync(), null, TimeSpan.Zero, NormalInterval);
    }

    private async Task UpdateAsync()
    {
        try
        {
            string url = $"{_fivemUrl.TrimEnd('/')}/players.json";
            var json = await _httpClient.GetStringAsync(url);
            var players = JsonSerializer.Deserialize<List<JsonElement>>(json);
            int playerCount = players?.Count ?? 0;

            // Success — reset fail counter
            _failCount = 0;

            if (playerCount == _lastPlayerCount)
            {
                await Log(LogSeverity.Debug, $"⏸️ Player count unchanged ({playerCount}), skipping update.");
                return;
            }

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

            await channel.ModifyAsync(props => props.Name = newName);
            _lastPlayerCount = playerCount;

            await Log(LogSeverity.Info, $"🔄 Channel name updated to: {newName}");
        }
        catch (Exception ex)
        {
            _failCount++;
            await Log(LogSeverity.Error, $"❌ Error updating FiveM channel: {ex.Message}");

            if (_failCount >= MaxFailsBeforePause)
            {
                await Log(LogSeverity.Warning, $"⚠️ {_failCount} failed attempts in a row. Pausing for {PauseInterval.TotalMinutes} minutes...");
                _timer?.Change(PauseInterval, NormalInterval);
                _failCount = 0; // reset after pausing
            }
        }
    }

    private Task Log(LogSeverity severity, string message)
    {
        Console.WriteLine(message);
        return _logFunc?.Invoke(new LogMessage(severity, "FiveM", message)) ?? Task.CompletedTask;
    }
}
