// File: MDTIncidentsCommand.cs
using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

public class MDTIncidentsCommand : ISlashCommand
{
    public string Name => "mdtincidents";
    public string Description => "View MDT incidents by day / week / month (Police Command only)";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // Police command roles
    private static readonly HashSet<ulong> PoliceCommandRoleIds = new()
    {
        1394459419156418730, // Response Inspector
        1394650413361533009, // Roads Inspector
        1394465316817473646, // TFU Inspector
        1394649657644290078, // Chief Inspector
        1394458024503935006, // Superintendent
        1394457219298492527, // Commissioner
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller)
        {
            await ReplyEphemeral(command, "❌ This command must be used in a server.");
            return;
        }

        // Permission check
        bool allowed = caller.Roles.Any(r => PoliceCommandRoleIds.Contains(r.Id));
        if (!allowed)
        {
            await ReplyEphemeral(command, "❌ You must be Police Command to use this command.");
            return;
        }

        // Validate option
        if (command.Data.Options == null || command.Data.Options.Count == 0)
        {
            await ReplyEphemeral(command, "❌ Usage: `/mdtincidents range:<day|week|month>`");
            return;
        }

        var range = command.Data.Options.First().Value?.ToString()?.ToLowerInvariant();
        if (range is not ("day" or "week" or "month"))
        {
            await ReplyEphemeral(command, "❌ Invalid range. Use `day`, `week`, or `month`.");
            return;
        }

        // ✅ Public defer for successful embed only
        await command.DeferAsync(ephemeral: false);

        DateTime fromDate = range switch
        {
            "day" => DateTime.UtcNow.AddDays(-1),
            "week" => DateTime.UtcNow.AddDays(-7),
            "month" => DateTime.UtcNow.AddMonths(-1),
            _ => DateTime.UtcNow.AddDays(-1)
        };

        var incidents = new List<IncidentRow>();

        const string incidentSql = @"
            SELECT id, name, cops, createdAt
            FROM mdt_incidents
            WHERE createdAt >= @from
            ORDER BY createdAt DESC;";

        try
        {
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();

            using var cmd = new MySqlCommand(incidentSql, conn);
            cmd.Parameters.AddWithValue("@from", fromDate);

            using var reader = await cmd.ExecuteReaderAsync();

            int idCol = reader.GetOrdinal("id");
            int nameCol = reader.GetOrdinal("name");
            int copsCol = reader.GetOrdinal("cops");
            int createdAtCol = reader.GetOrdinal("createdAt");

            while (await reader.ReadAsync())
            {
                incidents.Add(new IncidentRow
                {
                    Id = reader.GetInt32(idCol),
                    Name = reader.GetString(nameCol),
                    CopsJson = reader.GetString(copsCol),
                    CreatedAt = reader.GetDateTime(createdAtCol)
                });
            }

            if (!incidents.Any())
            {
                await ReplyEphemeral(command, $"ℹ️ No MDT incidents found for the last {Cap(range)}.");
                return;
            }

            var citizenIds = incidents
                .SelectMany(i => ParseJsonArray(i.CopsJson))
                .Distinct()
                .ToList();

            var discordMap = await LoadDiscordIds(citizenIds);

            var embed = new EmbedBuilder()
                .WithTitle($"📋 MDT Incidents — Last {Cap(range)}")
                .WithColor(new Color(0x1E, 0x40, 0xAF)) // Police blue
                .WithCurrentTimestamp();

            foreach (var inc in incidents.Take(10))
            {
                var cops = ParseJsonArray(inc.CopsJson);

                string officers = cops.Count == 0
                    ? "_Unknown_"
                    : string.Join(", ",
                        cops.Select(cid =>
                            discordMap.TryGetValue(cid, out var did)
                                ? $"<@{did}>"
                                : $"`{cid}`"));

                embed.AddField(
                    $"🗂 {inc.Name}",
                    $"👮 **Officer(s):** {officers}\n🕒 **Time:** <t:{ToUnix(inc.CreatedAt)}:f>",
                    false
                );
            }

            await command.ModifyOriginalResponseAsync(m =>
            {
                m.Content = string.Empty;
                m.Embed = embed.Build();
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            await ReplyEphemeral(command, "❌ Error retrieving MDT incidents.");
        }
    }

    // ---------- HELPERS ----------
    private static List<string> ParseJsonArray(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static async Task<Dictionary<string, string>> LoadDiscordIds(List<string> citizenIds)
    {
        var map = new Dictionary<string, string>();
        if (citizenIds.Count == 0) return map;

        var parameters = citizenIds.Select((_, i) => $"@cid{i}");
        var sql = $@"
            SELECT citizenid, discordid
            FROM datadiscord
            WHERE citizenid IN ({string.Join(",", parameters)});";

        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        using var cmd = new MySqlCommand(sql, conn);
        for (int i = 0; i < citizenIds.Count; i++)
            cmd.Parameters.AddWithValue($"@cid{i}", citizenIds[i]);

        using var reader = await cmd.ExecuteReaderAsync();

        int cidCol = reader.GetOrdinal("citizenid");
        int didCol = reader.GetOrdinal("discordid");

        while (await reader.ReadAsync())
        {
            map[reader.GetString(cidCol)] = reader.GetString(didCol);
        }

        return map;
    }

    private static long ToUnix(DateTime dt) =>
        new DateTimeOffset(dt).ToUnixTimeSeconds();

    private static string Cap(string s) =>
        char.ToUpper(s[0]) + s[1..];

    private static Task ReplyEphemeral(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
            m.Flags = MessageFlags.Ephemeral;
        });

    private class IncidentRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string CopsJson { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
