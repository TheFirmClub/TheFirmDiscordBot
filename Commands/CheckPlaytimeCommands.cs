using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;

public class CheckPlaytimeCommands : ISlashCommand
{
    public string Name => "checkplaytimecommands";
    public string Description => "Handles /myplaytime and /checkplaytime";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private const ulong SeniorManagementRoleId = 1393590761953558608;
    private const ulong GameModeratorRoleId    = 1393729574537396355;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        switch (command.Data.Name.ToLower())
        {
            case "myplaytime":
                await MyPlaytime(command);
                break;

            case "checkplaytime":
                await CheckPlaytime(command);
                break;
        }
    }

    // --------------------------------------------------------
    //  /myplaytime   (public)
    // --------------------------------------------------------
    private async Task MyPlaytime(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: false); // PUBLIC

        if (command.User is not SocketGuildUser caller)
        {
            await Reply(command, "❌ Must be used in a server.");
            return;
        }

        var row = await FetchAsync(caller.Id.ToString());
        if (row == null)
        {
            await Reply(command, "ℹ️ No playtime found for your account.");
            return;
        }

        var embed = BuildUnifiedEmbed(row, "🎮 My Playtime", caller.DisplayName);

        await command.ModifyOriginalResponseAsync(m =>
        {
            m.Content = "";
            m.Embed = embed;
        });
    }

    // --------------------------------------------------------
    //  /checkplaytime <discordid>   (restricted)
    // --------------------------------------------------------
    private async Task CheckPlaytime(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: false); // STAFF ONLY

        if (command.User is not SocketGuildUser caller)
        {
            await Reply(command, "❌ Must be used in a server.");
            return;
        }

        bool allowed =
            caller.Roles.Any(r => r.Id == SeniorManagementRoleId) ||
            caller.Roles.Any(r => r.Id == GameModeratorRoleId);

        if (!allowed)
        {
            await Reply(command, "❌ You are not allowed to use this command.");
            return;
        }

        var discordId = command.Data.Options.First().Value?.ToString();
        if (string.IsNullOrWhiteSpace(discordId))
        {
            await Reply(command, "❌ Provide a valid Discord ID.");
            return;
        }

        var row = await FetchAsync(discordId);
        if (row == null)
        {
            await Reply(command, $"ℹ️ No playtime found for `{discordId}`.");
            return;
        }

        var embed = BuildUnifiedEmbed(row, "🎮 Player Playtime Lookup", caller.DisplayName);

        await command.ModifyOriginalResponseAsync(m =>
        {
            m.Content = "";
            m.Embed = embed;
        });
    }

    // --------------------------------------------------------
    //  Database Fetch
    // --------------------------------------------------------
    private async Task<PlayerData?> FetchAsync(string discordId)
    {
        const string sql = @"
            SELECT license, display_name, discord_id, play_time, ts_last_connection, ts_joined, updated_at
            FROM player_playtime
            WHERE discord_id = @id
            ORDER BY ts_last_connection DESC
            LIMIT 1;
        ";

        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", discordId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new PlayerData
        {
            License          = reader["license"]?.ToString() ?? "",
            DisplayName      = reader["display_name"]?.ToString() ?? "",
            DiscordId        = reader["discord_id"]?.ToString() ?? discordId,
            PlayTimeMinutes  = Convert.ToInt32(reader["play_time"]),
            TsLastConnection = Convert.ToInt64(reader["ts_last_connection"]),
            TsJoined         = Convert.ToInt64(reader["ts_joined"]),
            UpdatedAt        = Convert.ToDateTime(reader["updated_at"])
        };
    }

    // --------------------------------------------------------
    //  Shared Embed Builder
    // --------------------------------------------------------
    private Embed BuildUnifiedEmbed(PlayerData row, string title, string requestedBy)
    {
        int minutes = row.PlayTimeMinutes;
        int hours = minutes / 60;
        int mins = minutes % 60;

        int days = hours / 24;
        int remHours = hours % 24;

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription($"Stats for **{row.DisplayName}** (`{row.DiscordId}`)")
            .AddField("Total Minutes", $"{minutes:N0} minutes", true)
            .AddField("Total Hours", $"{hours}h {mins}m", true)
            .AddField("Total Days", $"{days}d {remHours}h {mins}m", true)
            .AddField("First Joined", Unix(row.TsJoined), true)
            .AddField("Last Connected", Unix(row.TsLastConnection), true)
            .AddField("Updated On", row.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"), true)
            .WithFooter($"Requested by {requestedBy}")
            .WithCurrentTimestamp()
            .WithColor(new Color(0x22C55E))
            .Build();
    }

    private static string Unix(long ts)
    {
        return ts <= 0
            ? "Unknown"
            : DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static Task Reply(SocketSlashCommand command, string text)
        => command.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });

    private sealed class PlayerData
    {
        public string License { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DiscordId { get; set; } = "";
        public int PlayTimeMinutes { get; set; }
        public long TsLastConnection { get; set; }
        public long TsJoined { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
