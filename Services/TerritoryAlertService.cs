using Discord;
using Discord.WebSocket;
using MySqlConnector;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

public class TerritoryAlertService
{
    private readonly DiscordSocketClient _client;

    private const ulong DrugChannelId = 1468993727946166313UL;

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore2;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    private readonly Dictionary<int, ulong> _gangChannels = new()
    {
        {16, 1499490571310465065}, // Wearside Firm
        
    };

    public TerritoryAlertService(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessage;
    }

    // ================= MESSAGE HANDLER =================

    private async Task OnMessage(SocketMessage msg)
    {
        if (msg.Channel.Id != DrugChannelId || !msg.Author.IsWebhook)
            return;

        if (msg.Embeds.Count == 0)
            return;

        Console.WriteLine("🔥 DRUG SALE DETECTED");

        var embed = msg.Embeds.First();

        var rawText = $"{embed.Title}\n{embed.Description}";
        var text = CleanDiscordMarkdown(rawText);

        int zoneId = ExtractZone(text);
        string? identifier = ExtractIdentifier(text);

        if (zoneId == -1 || identifier == null)
        {
            Console.WriteLine("❌ Failed to parse embed.");
            return;
        }

        Console.WriteLine($"ZONE: {zoneId}");
        Console.WriteLine($"IDENTIFIER: {identifier}");

        // ✅ FIRST — load gang
        var playerGang = await GetPlayerGang(identifier);

        if (playerGang == null)
        {
            Console.WriteLine("❌ Player gang not found.");
            return;
        }

        // ✅ NOW cooldown is safe
        var cooldownKey = $"zone:{zoneId}|gang:{playerGang.Id}";

        if (_cooldowns.TryGetValue(cooldownKey, out var last))
        {
            if ((DateTime.UtcNow - last).TotalMinutes < 30)
            {
                Console.WriteLine("Cooldown active — skipping.");
                return;
            }
        }

        var turf = await GetTurfZone(zoneId);

        if (turf == null)
            return;

        var owner = turf.LoyalityList
            .OrderByDescending(x => x.InfluencePoints)
            .FirstOrDefault();

        if (owner == null)
        {
            Console.WriteLine("❌ Turf owner not found.");
            return;
        }

        if (owner.JobId == playerGang.Id)
        {
            Console.WriteLine("Selling inside own turf — ignoring.");
            return;
        }

        _cooldowns[cooldownKey] = DateTime.UtcNow;

        await SendAlert(turf, owner, playerGang);
    }

    // ================= CLEAN MARKDOWN =================

    private string CleanDiscordMarkdown(string text)
    {
        return text.Replace("**", "")
                   .Replace("__", "")
                   .Replace("*", "")
                   .Replace("`", "");
    }

    // ================= PLAYER GANG =================

    private async Task<Gang?> GetPlayerGang(string identifier)
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(@"
            SELECT o.id, o.label
            FROM opcrime_players p
            JOIN opcrime_orgs o ON p.jobId = o.id
            WHERE p.identificator = @id
            LIMIT 1;", conn);

        cmd.Parameters.AddWithValue("@id", identifier);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.Read())
            return null;

        return new Gang
        {
            Id = Convert.ToInt32(reader["id"]),
            Label = reader["label"].ToString()!
        };
    }

    // ================= TURF =================

    private async Task<TurfZone?> GetTurfZone(int zoneId)
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(@"
            SELECT `index`, label, loyalityList
            FROM opcrime_turfzones
            WHERE `index` = @zone
            LIMIT 1;", conn);

        cmd.Parameters.AddWithValue("@zone", zoneId);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.Read())
        {
            Console.WriteLine($"❌ Turf {zoneId} not found.");
            return null;
        }

        return new TurfZone
        {
            Index = Convert.ToInt32(reader["index"]),
            Label = reader["label"].ToString()!,
            LoyalityList = JsonSerializer.Deserialize<List<Loyalty>>(
                reader["loyalityList"].ToString()!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<Loyalty>()
        };
    }

    // ================= ALERT =================

    private async Task SendAlert(TurfZone turf, Loyalty owner, Gang intruder)
    {
        if (!_gangChannels.TryGetValue(owner.JobId, out var channelId))
        {
            Console.WriteLine($"❌ No channel mapped for gang {owner.JobId}");
            return;
        }

        var channel = _client.GetChannel(channelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("❌ Gang channel not found.");
            return;
        }

        Console.WriteLine($"🚨 Alert sent to {owner.GangName}");

        var embed = new EmbedBuilder()
            .WithColor(Color.DarkRed)
            .WithTitle("⚠️ Hostile Territory Activity")

            // ✅ ONLY THESE FIELDS
            .AddField("📍 Zone", turf.Label, true)
            .AddField("🕒 Time", DateTime.Now.ToString("HH:mm:ss"), false)

            .WithFooter("Gang Intelligence System")
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    // ================= REGEX =================

    private int ExtractZone(string text)
    {
        var match = Regex.Match(text, @"Zone Id:\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }

    private string? ExtractIdentifier(string text)
    {
        var match = Regex.Match(text, @"\[(.*?)\]");
        return match.Success ? match.Groups[1].Value : null;
    }

    // ================= MODELS =================

    private class Gang
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }

    private class TurfZone
    {
        public int Index { get; set; }
        public string Label { get; set; } = "";
        public List<Loyalty> LoyalityList { get; set; } = new();
    }

    private class Loyalty
    {
        [JsonPropertyName("jobId")]
        public int JobId { get; set; }

        [JsonPropertyName("gangName")]
        public string GangName { get; set; } = "";

        [JsonPropertyName("influencePoints")]
        public int InfluencePoints { get; set; }
    }
}
