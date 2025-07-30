using Discord.WebSocket;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        Console.WriteLine("⏱️ FiveMChannelUpdater: Timer started");

        _timer = new Timer(async _ =>
        {
            try
            {
                using var httpClient = new HttpClient();
                var url = $"{_fivemUrl}/players.json";

                Console.WriteLine($"🌐 Fetching FiveM players from {url}");
                var json = await httpClient.GetStringAsync(url);

                var players = JsonSerializer.Deserialize<List<JsonElement>>(json);
                int playerCount = players?.Count ?? 0;

                Console.WriteLine($"✅ FiveM players online: {playerCount}");

                var guild = _client.GetGuild(_guildId);
                if (guild == null)
                {
                    Console.WriteLine($"❌ Guild not found: {_guildId}");
                    return;
                }

                var channel = guild.GetVoiceChannel(_channelId);
                if (channel == null)
                {
                    Console.WriteLine($"❌ Voice channel not found: {_channelId}");
                    return;
                }

                string newName = $"Online Players: {playerCount}";
                if (channel.Name != newName)
                {
                    await channel.ModifyAsync(props => props.Name = newName);
                    Console.WriteLine($"🔄 Channel name updated to: {newName}");
                }
                else
                {
                    Console.WriteLine("⏸️ Channel name already up to date, skipping.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating FiveM channel: {ex.Message}");
            }

        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30)); // every 30 seconds
    }
}
