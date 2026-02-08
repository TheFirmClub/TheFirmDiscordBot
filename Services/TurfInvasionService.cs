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

    // Prevent spam per zone
    private readonly ConcurrentDictionary<int, DateTime> _zoneCooldowns = new();

    // Prevent duplicate gateway events
    private readonly ConcurrentDictionary<ulong, bool> _processedMessages = new();

    // Gang -> Channel
    private readonly Dictionary<string, ulong> _gangChannels =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["E22"] = 1467204788138545264,
        ["Ferrari Crime Family"] = 1466582372299575326,
        ["GSC"] = 1469357015426928718,
        ["LOST MC"] = 1469655420506341590,
    };

    // Gang -> Role
    private readonly Dictionary<string, ulong> _gangRoles =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["E22"] = 1468016850120999166,
        ["Ferrari Crime Family"] = 1466582702789754950,
        ["GSC"] = 1469355919510077470,
        ["LOST MC"] = 1469654367593566349,
    };

    public TurfInvasionService(DiscordSocketClient client, string connectionString)
    {
        _client = client;
        _connectionString = connectionString;

        _client.MessageReceived += OnMessageReceived;
    }

    private async Task OnMessageReceived(SocketMessage message)
    {
        try
        {
            if (message.Channel.Id != SourceChannelId) return;
            if (!message.Author.IsBot) return;
            if (message.Embeds.Count == 0) return;

            // Prevent duplicate processing
            if (!_processedMessages.TryAdd(message.Id, true))
                return;

            var embed = message.Embeds.First();

            int zoneId = ExtractZoneId(embed);
            string citizenId = ExtractCitizenId(embed);
            string activity = ExtractActivity(embed);

            if (zoneId == 0 || citizenId == null)
                return;

            // Cooldown check (5 minutes recommended)
            if (_zoneCooldowns.TryGetValue(zoneId, out var last))
            {
                if ((DateTime.UtcNow - last).TotalMinutes < 5)
                    return;
            }

            var zoneData = await GetZoneData(zoneId);
            if (zoneData == null) return;

            var (owner, label) = zoneData.Value;

            string playerGang = await GetPlayerGang(citizenId);

            // Ignore friendly activity
            if (playerGang != null &&
                playerGang.Equals(owner, StringComparison.OrdinalIgnoreCase))
                return;

            _zoneCooldowns[zoneId] = DateTime.UtcNow;

            await SendAlert(owner, label, zoneId, activity);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TurfInvasionService] Error: {ex}");
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
            Console.WriteLine("Failed to parse loyalty JSON.");
            return null;
        }

        var owner = loyalties?
            .OrderByDescending(x => x.influencePoints)
            .FirstOrDefault()?.gangName;

        if (owner == null)
            return null;

        return (owner, label);
    }

    private async Task SendAlert(string owner, string zoneLabel, int zoneId, string activity)
    {
        if (!_gangChannels.TryGetValue(owner, out ulong channelId))
            return;

        if (_client.GetChannel(channelId) is not IMessageChannel channel)
            return;

        // Threat color logic
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
            .WithFooter("Secure your territory immediately.")
            .WithCurrentTimestamp()
            .Build();

        // Role ping
        if (_gangRoles.TryGetValue(owner, out ulong roleId))
        {
            await channel.SendMessageAsync(
                text: $"<@&{roleId}>",
                embed: embed,
                allowedMentions: AllowedMentions.All
            );
        }
        else
        {
            // fallback if role missing
            await channel.SendMessageAsync(embed: embed);
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
