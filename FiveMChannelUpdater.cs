using Discord;
using Discord.WebSocket;
using System.Text.Json;
using System.Threading;

public class FiveMChannelUpdater : IDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly string _fivemUrl;
    private readonly ulong _guildId;
    private readonly ulong _channelId;
    private readonly Func<LogMessage, Task>? _logFunc;

    private Timer? _timer;
    private static readonly HttpClient _httpClient = new();

    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private int _lastPlayerCount = -1;
    private int _failCount = 0;
    private DateTimeOffset _lastRenameUtc = DateTimeOffset.MinValue;
    private bool _disposed = false;

    private const int MaxFailsBeforePause = 5;
    private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PauseInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RenameCooldown = TimeSpan.FromMinutes(15);

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
        _ = Log(LogSeverity.Info, "⏱️ FiveMChannelUpdater started");

        _timer = new Timer(async _ => await UpdateAsync(), null, TimeSpan.Zero, NormalInterval);
    }

    private async Task UpdateAsync()
    {
        if (!await _updateLock.WaitAsync(0))
        {
            await Log(LogSeverity.Debug, "⏭️ Update already running, skipping.");
            return;
        }

        try
        {
            string url = $"{_fivemUrl.TrimEnd('/')}/players.json";
            var json = await _httpClient.GetStringAsync(url);
            var players = JsonSerializer.Deserialize<List<JsonElement>>(json);
            int playerCount = players?.Count ?? 0;

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

            if (channel.Name == newName)
            {
                _lastPlayerCount = playerCount;
                await Log(LogSeverity.Debug, "✅ Channel name already correct, skipping PATCH.");
                return;
            }

            var now = DateTimeOffset.UtcNow;

            if (now - _lastRenameUtc < RenameCooldown)
            {
                var remaining = RenameCooldown - (now - _lastRenameUtc);
                await Log(LogSeverity.Debug,
                    $"🧊 Rename cooldown active, skipping rename. Remaining: {remaining.TotalMinutes:F1} minutes.");
                return;
            }

            await channel.ModifyAsync(props => props.Name = newName);

            _lastPlayerCount = playerCount;
            _lastRenameUtc = now;

            await Log(LogSeverity.Info, $"🔄 Channel name updated to: {newName}");
        }
        catch (Exception ex)
        {
            _failCount++;
            await Log(LogSeverity.Error, $"❌ Error updating FiveM channel: {ex.Message}");

            if (_failCount >= MaxFailsBeforePause)
            {
                await Log(LogSeverity.Warning,
                    $"⚠️ {_failCount} failed attempts in a row. Pausing for {PauseInterval.TotalMinutes} minutes...");

                _timer?.Change(PauseInterval, NormalInterval);
                _failCount = 0;
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private Task Log(LogSeverity severity, string message)
    {
        Console.WriteLine(message);
        return _logFunc?.Invoke(new LogMessage(severity, "FiveM", message)) ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
        _updateLock.Dispose();
    }
}