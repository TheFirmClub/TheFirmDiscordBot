using Discord;
using Discord.WebSocket;
using MySqlConnector;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

public class TerritoryAlertService
{
    private readonly DiscordSocketClient _client;

    private const ulong DrugChannelId = 1468993727946166313UL;

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // Prevent spam alerts
    private readonly ConcurrentDictionary<int, DateTime> _zoneCooldown = new();

    // Gang -> Discord channel
    private readonly Dictionary<int, ulong> _gangChannels = new()
    {
        {1, 1469357015426928718},
        {2, 1466582372299575326},
        {3, 1467204788138545264},
        {4, 1469655420506341590},
    };

    public TerritoryAlertService(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessage;
    }

    // ================= MESSAGE HANDLER =================

    private async Task OnMessage(SocketMessage msg)
    {
        // FAST FILTER
        if (msg.Channel.Id != DrugChannelId || !msg.Author.IsWebhook)
            return;

        if (msg.Embeds.Count == 0)
            return;

        Console.WriteLine("🔥 DRUG SALE DETECTED");

        var embed = msg.Embeds.First();

        // Build text safely (future proof)
        var rawText = $"{embed.Title}\n{embed.Description}";
        var text = CleanDiscordMarkdown(rawText);

        Console.WriteLine(text);

        int zoneId = ExtractZone(text);
        string? identifier = ExtractIdentifier(text);

        Console.WriteLine($"ZONE: {zoneId}");
        Console.WriteLine($"IDENTIFIER: {identifier}");

        if (zoneId == -1 || identifier == null)
        {
            Console.WriteLine("❌ Failed to parse embed.");
            return;
        }

        // Cooldown (3 mins per zone)
        if (_zoneCooldown.TryGetValue(zoneId, out var last))
        {
            if ((DateTime.UtcNow - last).TotalMinutes < 3)
            {
                Console.WriteLine("Cooldown active — skipping.");
                return;
            }
        }

        var playerGang = await GetPlayerGang(identifier);

        if (playerGang == null)
        {
            Console.WriteLine("❌ Player gang not found.");
            return;
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

        _zoneCooldown[zoneId] = DateTime.UtcNow;

        await SendAlert(turf, owner, playerGang, identifier);
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

    // ================= TURF DIRECT QUERY =================

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
            Console.WriteLine($"❌ Turf {zoneId} not found in DB.");
            return null;
        }

        Console.WriteLine($"Turf {zoneId} loaded from DB.");

        return new TurfZone
        {
            Index = Convert.ToInt32(reader["index"]),
            Label = reader["label"].ToString()!,
            LoyalityList = JsonSerializer.Deserialize<List<Loyalty>>(
                reader["loyalityList"].ToString()!
            ) ?? new List<Loyalty>()
        };
    }

    // ================= ALERT =================

    private async Task SendAlert(TurfZone turf, Loyalty owner, Gang intruder, string identifier)
    {
        Console.WriteLine($"OWNER JOB ID: {owner.JobId}");

        if (!_gangChannels.TryGetValue(owner.JobId, out var channelId))
        {
            Console.WriteLine("❌ No gang channel configured.");
            return;
        }

        var channel = _client.GetChannel(channelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("❌ Gang channel not found.");
            return;
        }

        Console.WriteLine($"🚨 Sending alert to {owner.GangName}");

        var embed = new EmbedBuilder()
            .WithColor(Color.DarkRed)
            .WithTitle("⚠️ Territory Violation Detected")
            .AddField("Zone", turf.Label, true)
            .AddField("Owner", owner.GangName, true)
            .AddField("Intruding Gang", intruder.Label, true)
            .AddField("Player Identifier", identifier, false)
            .WithFooter("Gang Intelligence System")
            .WithCurrentTimestamp()
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
        public int JobId { get; set; }
        public string GangName { get; set; } = "";
        public int InfluencePoints { get; set; }
    }
}
