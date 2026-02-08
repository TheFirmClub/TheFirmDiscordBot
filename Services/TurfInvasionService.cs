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
    private readonly Func<LogMessage, Task>? _log;

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

    public TurfInvasionService(
        DiscordSocketClient client,
        string connectionString,
        Func<LogMessage, Task>? logFunc = null)
    {
        _client = client;
        _connectionString = connectionString;
        _log = logFunc;

        Console.WriteLine("🔥 TurfInvasionService CONSTRUCTED");

        // Attach listener immediately (LIKE YOUR WORKING SERVICE)
        _client.MessageReceived += OnMessageReceived;

        Console.WriteLine("🔥 Turf listener ATTACHED");
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
        Console.WriteLine($"MESSAGE RECEIVED FROM: {message.Channel.Id}");
        try
        {
            if (message.Channel.Id != SourceChannelId)
                return;

            if (message.Embeds.Count == 0)
                return;

            if (!_processedMessages.TryAdd(message.Id, true))
                return;

            var embed = message.Embeds.First();

            int zoneId = ExtractZoneId(embed);
            string citizenId = ExtractCitizenId(embed);
            string activity = ExtractActivity(embed);

            await _logSafe(LogSeverity.Info,
                $"📩 Turf event detected | Zone:{zoneId} | CID:{citizenId} | Activity:{activity}");

            if (zoneId == 0 || citizenId == null)
            {
                await _logSafe(LogSeverity.Warning,
                    "❌ Missing zoneId or citizenId — ignoring.");
                return;
            }

            // Cooldown
            if (_zoneCooldowns.TryGetValue(zoneId, out var last))
            {
                var diff = DateTime.UtcNow - last;

                if (diff.TotalMinutes < 5)
                {
                    await _logSafe(LogSeverity.Warning,
                        $"⛔ Cooldown active for zone {zoneId} ({diff.TotalSeconds:F0}s)");
                    return;
                }
            }

            var zoneData = await GetZoneData(zoneId);

            if (zoneData == null)
            {
                await _logSafe(LogSeverity.Warning,
                    $"❌ No zone data found for zone {zoneId}");
                return;
            }

            var (ownerRaw, label) = zoneData.Value;

            string owner = Normalize(ownerRaw);
            string playerGang = Normalize(await GetPlayerGang(citizenId));

            await _logSafe(LogSeverity.Info,
                $"Owner:'{owner}' vs PlayerGang:'{playerGang}'");

            // Friendly fire
            if (!string.IsNullOrWhiteSpace(playerGang) &&
                owner == playerGang)
            {
                await _logSafe(LogSeverity.Info,
                    "✅ Friendly activity — ignored.");
                return;
            }

            await _logSafe(LogSeverity.Info,
                $"🚨 ALERT TRIGGERED for {label}");

            _zoneCooldowns[zoneId] = DateTime.UtcNow;

            await SendAlert(owner, label, zoneId, activity);
        }
        catch (Exception ex)
        {
            await _logSafe(LogSeverity.Error,
                $"🔥 TurfInvasionService ERROR: {ex}");
        }
    }

    private async Task _logSafe(LogSeverity severity, string message)
    {
        if (_log != null)
            await _log.Invoke(new LogMessage(severity, "Turf", message));
    }

    private int ExtractZoneId(Embed embed)
    {
        foreach (var field in embed.Fields)
        {
            if (field.Name.Contains("Zone", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(field.Value, out int zone))
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

        string loyaltyJson =
            reader.IsDBNull(reader.GetOrdinal("loyalityList"))
                ? null
                : reader.GetString(reader.GetOrdinal("loyalityList"));

        string label =
            reader.IsDBNull(reader.GetOrdinal("label"))
                ? "Unknown"
                : reader.GetString(reader.GetOrdinal("label"));

        try
        {
            var loyalties =
                JsonSerializer.Deserialize<List<Loyalty>>(loyaltyJson);

            var owner = loyalties?
                .OrderByDescending(x => x.influencePoints)
                .FirstOrDefault()?.gangName;

            return (owner, label);
        }
        catch
        {
            await _logSafe(LogSeverity.Error,
                "❌ Failed to parse loyalty JSON.");
            return null;
        }
    }

    private async Task SendAlert(string owner, string zoneLabel, int zoneId, string activity)
    {
        if (!_gangChannels.TryGetValue(owner, out ulong channelId))
        {
            await _logSafe(LogSeverity.Warning,
                $"❌ No channel mapped for gang '{owner}'");
            return;
        }

        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            await _logSafe(LogSeverity.Error,
                $"❌ Discord channel not found for '{owner}'");
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
            .WithCurrentTimestamp()
            .Build();

        try
        {
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

            await _logSafe(LogSeverity.Info,
                $"✅ Alert sent to '{owner}' for zone {zoneLabel}");
        }
        catch (Exception ex)
        {
            await _logSafe(LogSeverity.Error,
                $"🔥 Failed sending Discord alert: {ex}");
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
