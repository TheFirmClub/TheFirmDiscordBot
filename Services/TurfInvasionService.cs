using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;

public class TurfInvasionService
{
    private readonly DiscordSocketClient _client;

    // ✅ INLINE SQL (FOR DEBUGGING ONLY)
    private readonly string _connectionString =
        "Server=YOUR_NEW_PASSWORD_HERE;" +
        "Port=35666;" +
        "Database=thefirm_qbcore;" +
        "Uid=thefirmprod;" +
        "Pwd=edr6BYZqmq7eud0mwm;" +
        "CharSet=utf8mb4;" +
        "SslMode=Preferred;";

    private const ulong SourceChannelId = 1468993727946166313;

    private readonly ConcurrentDictionary<int, DateTime> _zoneCooldowns = new();
    private readonly ConcurrentDictionary<ulong, bool> _processedMessages = new();

    private readonly Dictionary<string, ulong> _gangChannels = new()
    {
        ["E22"] = 1467204788138545264,
        ["FERRARI CRIME FAMILY"] = 1466582372299575326,
        ["GSC"] = 1469357015426928718,
        ["LOST MC"] = 1469655420506341590,
    };

    private readonly Dictionary<string, ulong> _gangRoles = new()
    {
        ["E22"] = 1468016850120999166,
        ["FERRARI CRIME FAMILY"] = 1466582702789754950,
        ["GSC"] = 1469355919510077470,
        ["LOST MC"] = 1469654367593566349,
    };

    public TurfInvasionService(DiscordSocketClient client)
    {
        _client = client;

        Console.WriteLine("🔥 TurfInvasionService STARTED");

        // Attach immediately
        _client.MessageReceived += OnMessageReceived;
    }

    private string Normalize(string value)
    {
        return value?
            .Trim()
            .Replace("\u200B", "")
            .ToUpperInvariant();
    }

    private async Task OnMessageReceived(SocketMessage message)
    {
        try
        {
            // ⭐ GLOBAL DEBUG
            Console.WriteLine($"MSG → Channel:{message.Channel.Id}");

            if (message.Channel.Id != SourceChannelId)
                return;

            // Accept bots AND webhooks
            if (!message.Author.IsBot && !message.Author.IsWebhook)
                return;

            if (message.Embeds.Count == 0)
                return;

            if (!_processedMessages.TryAdd(message.Id, true))
                return;

            var embed = message.Embeds.First();

            int zoneId = ExtractZoneId(embed);
            string citizenId = ExtractCitizenId(embed);
            string activity = ExtractActivity(embed);

            Console.WriteLine($"🔥 Turf Trigger → Zone:{zoneId} CID:{citizenId}");

            if (zoneId == 0 || citizenId == null)
                return;

            // Cooldown
            if (_zoneCooldowns.TryGetValue(zoneId, out var last))
            {
                if ((DateTime.UtcNow - last).TotalMinutes < 5)
                    return;
            }

            var zoneData = await GetZoneData(zoneId);

            if (zoneData == null)
                return;

            var (ownerRaw, label) = zoneData.Value;

            string owner = Normalize(ownerRaw);
            string playerGang = Normalize(await GetPlayerGang(citizenId));

            Console.WriteLine($"OWNER:{owner} vs PLAYER:{playerGang}");

            // Friendly activity
            if (!string.IsNullOrWhiteSpace(playerGang) &&
                owner == playerGang)
                return;

            _zoneCooldowns[zoneId] = DateTime.UtcNow;

            await SendAlert(owner, label, zoneId, activity);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Turf ERROR: {ex}");
        }
    }

    private int ExtractZoneId(Embed embed)
    {
        foreach (var field in embed.Fields)
        {
            if (field.Name.Contains("Zone", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(field.Value.Trim(), out int zone))
                return zone;
        }

        return 0;
    }

    private string ExtractCitizenId(Embed embed)
    {
        foreach (var field in embed.Fields)
        {
            if (!field.Name.Contains("Player"))
                continue;

            var match = Regex.Match(field.Value, @"\[(.*?)\]");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        return null;
    }

    private string ExtractActivity(Embed embed)
    {
        if (string.IsNullOrWhiteSpace(embed.Title))
            return "Suspicious Activity";

        return CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(embed.Title.ToLower());
    }

    private async Task<string?> GetPlayerGang(string citizenId)
    {
        using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
            SELECT o.label
            FROM opcrime_players p
            JOIN opcrime_orgs o ON p.jobId = o.id
            WHERE p.identificator = @cid
            LIMIT 1";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@cid", citizenId);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    private async Task<(string owner, string label)?> GetZoneData(int zoneId)
    {
        using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query =
            "SELECT loyalityList, label FROM opcrime_turfzones WHERE `index`=@zone LIMIT 1";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@zone", zoneId);

        using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        int loyaltyIndex = reader.GetOrdinal("loyalityList");
        int labelIndex = reader.GetOrdinal("label");

        string loyaltyJson = reader.IsDBNull(loyaltyIndex)
            ? null
            : reader.GetString(loyaltyIndex);

        string label = reader.IsDBNull(labelIndex)
            ? "Unknown"
            : reader.GetString(labelIndex);

        var loyalties =
            JsonSerializer.Deserialize<List<Loyalty>>(loyaltyJson);

        var owner = loyalties?
            .OrderByDescending(x => x.influencePoints)
            .FirstOrDefault()?.gangName;

        return (owner, label);
    }

    private async Task SendAlert(string owner, string zoneLabel, int zoneId, string activity)
    {
        if (!_gangChannels.TryGetValue(owner, out ulong channelId))
        {
            Console.WriteLine($"No channel for gang {owner}");
            return;
        }

        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"Channel missing for {owner}");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🚨 Territory Activity Detected")
            .WithColor(Color.DarkRed)
            .WithDescription($"**{activity}** detected inside **{zoneLabel}**.")
            .AddField("Zone ID", zoneId, true)
            .WithCurrentTimestamp()
            .Build();

        if (_gangRoles.TryGetValue(owner, out ulong roleId))
        {
            await channel.SendMessageAsync(
                $"<@&{roleId}>",
                embed: embed,
                allowedMentions: AllowedMentions.All);
        }
        else
        {
            await channel.SendMessageAsync(embed: embed);
        }

        Console.WriteLine($"ALERT SENT → {owner}");
    }

    private class Loyalty
    {
        public int jobId { get; set; }
        public string gangColor { get; set; }
        public string gangName { get; set; }
        public int influencePoints { get; set; }
    }
}
