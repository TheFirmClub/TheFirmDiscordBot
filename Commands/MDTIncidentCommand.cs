// File: MDTIncidentsCommand.cs
using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class MDTIncidentsCommand : ISlashCommand
{
    public string Name => "mdtincidents";
    public string Description => "View MDT incidents by day / week / month (Police Command only)";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore2;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private static readonly HashSet<ulong> PoliceCommandRoleIds = new()
    {
        1394459419156418730, // Response Inspector
        1394650413361533009, // Roads Inspector
        1394465316817473646, // TFU Inspector
        1394649657644290078, // Chief Inspector
        1394458024503935006, // Superintendent
        1394457219298492527, // Commissioner
        1393590761953558608, // SM
    };

    private const int PageSize = 10; // incidents per embed

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller)
        {
            await ReplyEphemeral(command, "❌ This command must be used in a server.");
            return;
        }

        // Role check
        bool allowed = caller.Roles.Any(r => PoliceCommandRoleIds.Contains(r.Id));
        if (!allowed)
        {
            await ReplyEphemeral(command, "❌ You must be Police Command to use this command.");
            return;
        }

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

        // Public defer
        try { await command.DeferAsync(ephemeral: false); } 
        catch { await command.RespondAsync("❌ Failed to defer command.", ephemeral: true); return; }

        DateTime fromDate = range switch
        {
            "day" => DateTime.UtcNow.AddDays(-1),
            "week" => DateTime.UtcNow.AddDays(-7),
            "month" => DateTime.UtcNow.AddMonths(-1),
            _ => DateTime.UtcNow.AddDays(-1)
        };

        var incidents = await LoadIncidents(fromDate);

        if (!incidents.Any())
        {
            await ReplyEphemeral(command, $"ℹ️ No MDT incidents found for the last {Cap(range)}.");
            return;
        }

        // Map citizenid -> discordid
        var citizenIds = incidents.SelectMany(i => ParseJsonArray(i.CopsJson)).Distinct().ToList();
        var discordMap = await LoadDiscordIds(citizenIds);

        // Build paginated embeds
        var embeds = BuildEmbeds(incidents, discordMap, range);

        // Buttons for pagination
        var builder = new ComponentBuilder()
            .WithButton("⬅️ Prev", "mdt_prev", disabled: true)
            .WithButton("Next ➡️", "mdt_next", disabled: embeds.Count <= 1);

        // Send first page
        var message = await command.FollowupAsync(
            embed: embeds[0],
            components: builder.Build(),
            ephemeral: false
        );

        // Store state in memory (for demo; in production use a proper cache)
        PaginationStore.Add(message.Id, new PaginationState
        {
            Embeds = embeds,
            CurrentPage = 0,
            OriginalUserId = caller.Id,
            Message = message,
            Builder = builder
        });
    }

    // ---------- Helpers ----------

    private static async Task<List<IncidentRow>> LoadIncidents(DateTime fromDate)
    {
        var list = new List<IncidentRow>();

        const string sql = @"
            SELECT id, name, cops, createdAt
            FROM mdt_incidents
            WHERE createdAt >= @from
            ORDER BY createdAt DESC;";

        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@from", fromDate);

        using var reader = await cmd.ExecuteReaderAsync();

        int idCol = reader.GetOrdinal("id");
        int nameCol = reader.GetOrdinal("name");
        int copsCol = reader.GetOrdinal("cops");
        int createdAtCol = reader.GetOrdinal("createdAt");

        while (await reader.ReadAsync())
        {
            list.Add(new IncidentRow
            {
                Id = reader.GetInt32(idCol),
                Name = reader.GetString(nameCol),
                CopsJson = reader.GetString(copsCol),
                CreatedAt = reader.GetDateTime(createdAtCol)
            });
        }

        return list;
    }

    private static List<string> ParseJsonArray(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static async Task<Dictionary<string, string>> LoadDiscordIds(List<string> citizenIds)
    {
        var map = new Dictionary<string, string>();
        if (!citizenIds.Any()) return map;

        var parameters = citizenIds.Select((_, i) => $"@cid{i}");
        var sql = $@"SELECT citizenid, discordid FROM datadiscord WHERE citizenid IN ({string.Join(",", parameters)});";

        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new MySqlCommand(sql, conn);

        for (int i = 0; i < citizenIds.Count; i++)
            cmd.Parameters.AddWithValue($"@cid{i}", citizenIds[i]);

        using var reader = await cmd.ExecuteReaderAsync();

        int cidCol = reader.GetOrdinal("citizenid");
        int didCol = reader.GetOrdinal("discordid");

        while (await reader.ReadAsync())
            map[reader.GetString(cidCol)] = reader.GetString(didCol);

        return map;
    }

    private static List<Embed> BuildEmbeds(List<IncidentRow> incidents, Dictionary<string, string> discordMap, string range)
    {
        var embeds = new List<Embed>();

        for (int i = 0; i < incidents.Count; i += PageSize)
        {
            var batch = incidents.Skip(i).Take(PageSize);

            var embed = new EmbedBuilder()
                .WithTitle($"📋 MDT Incidents — Last {Cap(range)}")
                .WithColor(new Color(0x1E, 0x40, 0xAF))
                .WithCurrentTimestamp();

            foreach (var inc in batch)
            {
                var cops = ParseJsonArray(inc.CopsJson);
                string officers = cops.Count == 0
                    ? "_Unknown_"
                    : string.Join(", ", cops.Select(cid =>
                        discordMap.TryGetValue(cid, out var did) ? $"<@{did}>" : $"`{cid}`"));

                if (officers.Length > 1000) officers = officers[..1000] + "…";

                embed.AddField($"🗂 {inc.Name}", $"👮 **Officer(s):** {officers}\n🕒 <t:{ToUnix(inc.CreatedAt)}:f>", false);
            }

            embeds.Add(embed.Build());
        }

        return embeds;
    }

    private static long ToUnix(DateTime dt) => new DateTimeOffset(dt).ToUnixTimeSeconds();
    private static string Cap(string s) => char.ToUpper(s[0]) + s[1..];
    private static Task ReplyEphemeral(SocketSlashCommand cmd, string text) =>
        cmd.RespondAsync(text, ephemeral: true);

    // ---------- Models ----------
    private class IncidentRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string CopsJson { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    // ---------- Pagination State ----------
    private class PaginationState
    {
        public List<Embed> Embeds { get; set; } = new();
        public int CurrentPage { get; set; } = 0;
        public ulong OriginalUserId { get; set; }
        public IUserMessage Message { get; set; } = null!;
        public ComponentBuilder Builder { get; set; } = null!;
    }

    // Simple in-memory store
    private static class PaginationStore
    {
        public static readonly Dictionary<ulong, PaginationState> Pages = new();

        public static void Add(ulong messageId, PaginationState state) => Pages[messageId] = state;
        public static bool TryGet(ulong messageId, out PaginationState state) => Pages.TryGetValue(messageId, out state);
    }

    // ---------- Handle button interaction ----------
    public static async Task HandleButton(SocketMessageComponent component)
    {
        if (component.Data.CustomId != "mdt_prev" && component.Data.CustomId != "mdt_next")
            return;

        if (!PaginationStore.TryGet(component.Message.Id, out var state))
            return;

        // Only original user
        if (component.User.Id != state.OriginalUserId)
        {
            await component.RespondAsync("❌ Only the command user can use these buttons.", ephemeral: true);
            return;
        }

        // Change page
        if (component.Data.CustomId == "mdt_next" && state.CurrentPage < state.Embeds.Count - 1)
            state.CurrentPage++;
        else if (component.Data.CustomId == "mdt_prev" && state.CurrentPage > 0)
            state.CurrentPage--;

        // Update buttons
        var newBuilder = new ComponentBuilder()
            .WithButton("⬅️ Prev", "mdt_prev", disabled: state.CurrentPage == 0)
            .WithButton("Next ➡️", "mdt_next", disabled: state.CurrentPage == state.Embeds.Count - 1);

        try
        {
            await component.UpdateAsync(msg =>
            {
                msg.Embed = state.Embeds[state.CurrentPage];
                msg.Components = newBuilder.Build();
            });
        }
        catch
        {
            // Fallback if UpdateAsync fails (rare)
            await component.RespondAsync("❌ Failed to update page.", ephemeral: true);
        }
    }
}
