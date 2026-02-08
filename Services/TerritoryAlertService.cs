using Discord;
using Discord.WebSocket;
using MySqlConnector;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

public class TerritoryAlertService
{
    private readonly DiscordSocketClient _client;

    // LISTEN HERE
    private const ulong DrugChannelId = 1468993727946166313UL;

    // ⚠️ TEST DB ONLY — rotate before production
    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // ===== CACHES =====
    private readonly Dictionary<int, Gang> _gangCache = new();
    private readonly Dictionary<int, TurfZone> _turfCache = new();

    // Prevent spam alerts
    private readonly ConcurrentDictionary<int, DateTime> _zoneCooldown = new();

    // Map gang -> discord channel
    // CHANGE THESE
    private readonly Dictionary<int, ulong> _gangChannels = new()
    {
        {1, 1469357015426928718}, // GSC channel
        {2, 1466582372299575326},  // Ferrari channel
        {3, 1467204788138545264},  // E22 channel
        {4, 1469655420506341590},  // LostMC channel
        
    };

    public TerritoryAlertService(DiscordSocketClient client)
    {
        _client = client;
        _client.Ready += OnReady;
        _client.MessageReceived += OnMessage;
    }

    // ================= STARTUP =================

    private async Task OnReady()
    {
        Console.WriteLine("TerritoryAlertService starting...");

        await LoadGangCache();
        await LoadTurfCache();

        Console.WriteLine($"Loaded {_gangCache.Count} gangs.");
        Console.WriteLine($"Loaded {_turfCache.Count} turf zones.");
        Console.WriteLine("TerritoryAlertService READY.");
    }

    // ================= MESSAGE HANDLER =================

    private async Task OnMessage(SocketMessage msg)
    
    {
        Console.WriteLine("Territory service saw a message.");
        Console.WriteLine($"Channel: {msg.Channel.Id}");
        Console.WriteLine($"Author: {msg.Author}");
        Console.WriteLine($"IsWebhook: {msg.Author.IsWebhook}");
        Console.WriteLine($"Content: {msg.Content}");
        Console.WriteLine($"Embeds Count: {msg.Embeds.Count}");
        

        if (msg.Channel.Id != DrugChannelId)
            return;
        Console.WriteLine("🔥 DRUG CHANNEL HIT 🔥");

        if (msg.Embeds.Count == 0)
            return;

        var embed = msg.Embeds.First();
        var text = embed.Description ?? embed.Title ?? "";

        if (!text.Contains("DRUG SOLD"))
            return;

        int zoneId = ExtractZone(text);
        string? identifier = ExtractIdentifier(text);

        if (zoneId == -1 || identifier == null)
            return;

        // Cooldown (3 minutes per zone)
        if (_zoneCooldown.TryGetValue(zoneId, out var last))
        {
            if ((DateTime.UtcNow - last).TotalMinutes < 3)
                return;
        }

        var playerGang = await GetPlayerGang(identifier);
        if (playerGang == null)
            return;

        if (!_turfCache.TryGetValue(zoneId, out var turf))
            return;

        var owner = turf.LoyalityList
            .OrderByDescending(x => x.InfluencePoints)
            .FirstOrDefault();

        if (owner == null)
            return;

        // Same gang -> allowed
        if (owner.JobId == playerGang.Id)
            return;

        _zoneCooldown[zoneId] = DateTime.UtcNow;

        await SendAlert(turf, owner, playerGang, identifier);
    }

    // ================= DATABASE =================

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
            Id = reader.GetInt32("id"),
            Label = reader.GetString("label")
        };
    }

    private async Task LoadGangCache()
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand("SELECT id, label FROM opcrime_orgs;", conn);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var gang = new Gang
            {
                Id = reader.GetInt32("id"),
                Label = reader.GetString("label")
            };

            _gangCache[gang.Id] = gang;
        }
    }

    private async Task LoadTurfCache()
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand("SELECT `index`, label, loyalityList FROM opcrime_turfzones;", conn);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var zone = new TurfZone
            {
                Index = reader.GetInt32("index"),
                Label = reader.GetString("label")
            };

            var loyaltyJson = reader.GetString("loyalityList");

            zone.LoyalityList =
                JsonSerializer.Deserialize<List<Loyalty>>(loyaltyJson)
                ?? new List<Loyalty>();

            _turfCache[zone.Index] = zone;
        }
    }

    // ================= ALERT =================

    private async Task SendAlert(TurfZone turf, Loyalty owner, Gang intruder, string identifier)
    {
        if (!_gangChannels.TryGetValue(owner.JobId, out var channelId))
            return;

        var channel = _client.GetChannel(channelId) as IMessageChannel;
        if (channel == null) return;

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
        var match = Regex.Match(text, @"Zone Id:\s*(\d+)");
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
