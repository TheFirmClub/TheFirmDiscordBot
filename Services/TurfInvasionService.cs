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
    private readonly string _connectionString;

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

    public TurfInvasionService(DiscordSocketClient client, string connectionString)
    {
        _client = client;
        _connectionString = connectionString;

        Console.WriteLine("✅ TurfInvasionService initialized");

        _client.MessageReceived += OnMessageReceived;
    }

    private string Normalize(string value)
    {
        return value?
            .Trim()
            .Replace("\u200B", "") // kills zero-width spaces
            .ToUpperInvariant();
    }

    private async Task OnMessageReceived(SocketMessage message)
    {
        try
        {
            Console.WriteLine("📩 Message received");

            if (message.Channel.Id != SourceChannelId)
                return;

            if (!message.Author.IsBot)
                return;

            if (message.Embeds.Count == 0)
                return;

            if (!_processedMessages.TryAdd(message.Id, true))
            {
                Console.WriteLine("⚠️ Duplicate message ignored.");
                return;
            }

            var embed = message.Embeds.First();

            int zoneId = ExtractZoneId(embed);
            string citizenId = ExtractCitizenId(embed);
            string activity = ExtractActivity(embed);

            Console.WriteLine($"ZONE: {zoneId}");
            Console.WriteLine($"CitizenID: {citizenId}");
            Console.WriteLine($"Activity: {activity}");

            if (zoneId == 0 || citizenId == null)
            {
                Console.WriteLine("❌ Missing zone or citizenId.");
                return;
            }

            // Cooldown check
            if (_zoneCooldowns.TryGetValue(zoneId, out var last))
            {
                var diff = DateTime.UtcNow - last;

                if (diff.TotalMinutes < 1)
                {
                    Console.WriteLine($"⛔ Cooldown active ({diff.TotalSeconds:F0}s ago)");
                    return;
                }
            }

            var zoneData = await GetZoneData(zoneId);

            if (zoneData == null)
            {
                Console.WriteLine("❌ Zone data returned null.");
                return;
            }

            var (ownerRaw, label) = zoneData.Value;

            string owner = Normalize(ownerRaw);

            Console.WriteLine($"Owner RAW:'{ownerRaw}' Length:{ownerRaw?.Length}");
            Console.WriteLine($"Owner NORMALIZED:'{owner}'");

            string playerGangRaw = await GetPlayerGang(citizenId);
            string playerGang = Normalize(playerGangRaw);

            Console.WriteLine($"PlayerGang RAW:'{playerGangRaw}'");
            Console.WriteLine($"PlayerGang NORMALIZED:'{playerGang}'");

            // Friendly fire check
            if (!string.IsNullOrWhiteSpace(playerGang) &&
                playerGang == owner)
            {
                Console.WriteLine("✅ Friendly activity detected — ignoring.");
                return;
            }

            Console.WriteLine("🚨 ALERT TRIGGERED");

            _zoneCooldowns[zoneId] = DateTime.UtcNow;

            await SendAlert(owner, label, zoneId, activity);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🔥 TurfInvasionService ERROR: {ex}");
        }
    }

    private int ExtractZoneId(Embed embed)
    {
        foreach (var field in embed.Fields)
        {
            if (field.Name.Contains("Zone", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(field.Value, out int zone))
                    return zone;
            }
        }
        return 0;
    }

    private string ExtractCitizenId(Embed embed)
    {
        foreach (var field in embed.Fields)
        {
            if (!field.Name.Contains("Player")) continue;

            var match = Regex.Match(field.Value, @"\[(.*?)\]");
            if (match.Success)
                return match.Groups[1].Value;
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

        string query = @"
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

        string query = "SELECT loyalityList, label FROM opcrime_turfzones WHERE `index`=@zone LIMIT 1";

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

        List<Loyalty>? loyalties;

        try
        {
            loyalties = JsonSerializer.Deserialize<List<Loyalty>>(loyaltyJson);
        }
        catch
        {
            Console.WriteLine("❌ Failed to parse loyalty JSON.");
            return null;
        }

        var owner = loyalties?
            .OrderByDescending(x => x.influencePoints)
            .FirstOrDefault()?.gangName;

        return (owner, label);
    }

    private async Task SendAlert(string owner, string zoneLabel, int zoneId, string activity)
    {
        Console.WriteLine($"Sending alert to gang: '{owner}'");

        if (!_gangChannels.TryGetValue(owner, out ulong channelId))
        {
            Console.WriteLine($"❌ No channel mapped for gang '{owner}'");
            return;
        }

        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine("❌ Channel not found.");
            return;
        }

        Color color =
            activity.Contains("Graffiti", StringComparison.OrdinalIgnoreCase)
                ? new Color(255, 200, 0)
            : activity.Contains("Drug", StringComparison.OrdinalIgnoreCase)
                ? new Color(255, 140, 0)
                : Color.DarkRed;

        var embed = new EmbedBuilder()
            .WithTitle("🚨 Territory Activity Detected")
            .WithColor(color)
            .WithDescription($"**{activity}** detected inside **{zoneLabel}**.")
            .AddField("Zone ID", zoneId, true)
            .AddField("Activity", activity, true)
            .WithCurrentTimestamp()
            .Build();

        try
        {
            if (_gangRoles.TryGetValue(owner, out ulong roleId))
            {
                Console.WriteLine($"Pinging role {roleId}");

                await channel.SendMessageAsync(
                    text: $"<@&{roleId}>",
                    embed: embed,
                    allowedMentions: AllowedMentions.All
                );
            }
            else
            {
                Console.WriteLine("⚠️ No role mapped — sending embed only.");
                await channel.SendMessageAsync(embed: embed);
            }

            Console.WriteLine("✅ Alert sent successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🔥 Failed to send alert: {ex}");
        }
    }

    private class Loyalty
    {
        public int jobId { get; set; }
        public string gangColor { get; set; }
        public string gangName { get; set; }
        public int influencePoints { get; set; }
    }
}
