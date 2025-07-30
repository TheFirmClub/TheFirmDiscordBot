using Discord.WebSocket;
using System.Net.Http;
using System.Text.Json;

public class FiveMChannelUpdater
{
    private readonly DiscordSocketClient _client;
    private readonly string _fivemUrl;
    private readonly ulong _guildId;
    private readonly ulong _channelId;
    private Timer? _timer;

    public FiveMChannelUpdater(DiscordSocketClient client, string fivemUrl, ulong guildId, ulong channelId)
    {
        _client = client;
        _fivemUrl = fivemUrl;
        _guildId = guildId;
        _channelId = channelId;
    }

    public void Start()
    {
        _timer = new Timer(async _ =>
        {
            try
            {
                using var httpClient = new HttpClient();
                var json = await httpClient.GetStringAsync($"{_fivemUrl}/players.json");
                var players = JsonSerializer.Deserialize<List<object>>(json);
                int playerCount = players?.Count ?? 0;

                var guild = _client.GetGuild(_guildId);
                var channel = guild?.GetVoiceChannel(_channelId);

                if (channel != null && channel.Name != $"Online Players: {playerCount}")
                {
                    await channel.ModifyAsync(props => props.Name = $"Online Players: {playerCount}");
                    Console.WriteLine($"✅ Updated channel to: Online Players: {playerCount}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to update FiveM player count: {ex.Message}");
            }

        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30)); // every 30 seconds
    }
}
