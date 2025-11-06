// File: PlaytimeCommand.cs
using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class PlaytimeCommand : ISlashCommand
{
    public string Name => "playtime";
    public string Description => "Check police or ambulance playtime by CID";

    // --- DB: no fallbacks, goes straight to your OVH MySQL ---
    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // --- Role gates ---
    private const ulong SeniorManagementRoleId = 1393590761953558608; // Senior Management
    private const ulong SeniorModeratorRoleId  = 1393638449709584434; // Senior Moderator

    private static readonly HashSet<ulong> PoliceLeadershipRoleIds = new()
    {
        1394649657644290078, // Chief Inspector
        1394458024503935006, // Superintendent
        1394457219298492527, // Commissioner
    };

    private static readonly HashSet<ulong> MedicalLeadershipRoleIds = new()
    {
        1398308435795251302, // COO
        1394460689338208296, // Medical Director
        1394460400988454953, // CMO
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        // Must be used in a guild
        if (command.User is not SocketGuildUser caller ||
            (command.Channel as SocketGuildChannel)?.Guild == null)
        {
            await Reply(command, "❌ This command must be used in a server.");
            return;
        }

        // Expect one subcommand: "police" or "ambulance", and an option "cid"
        if (command.Data.Options is null || command.Data.Options.Count == 0)
        {
            await Reply(command, "❌ Usage: `/playtime police <CID>` or `/playtime ambulance <CID>`");
            return;
        }

        var sub = command.Data.Options.First(); // subcommand
        var subName = sub.Name?.ToLowerInvariant();

        if (subName != "police" && subName != "ambulance")
        {
            await Reply(command, "❌ Unknown subcommand. Use `police` or `ambulance`.");
            return;
        }

        // Authorization: SM + Senior Mod always allowed; Police leaders for police; Medical leaders for ambulance
        bool isSeniorManagement = caller.Roles.Any(r => r.Id == SeniorManagementRoleId);
        bool isSeniorModerator  = caller.Roles.Any(r => r.Id == SeniorModeratorRoleId);
        bool isPoliceLeader     = caller.Roles.Any(r => PoliceLeadershipRoleIds.Contains(r.Id));
        bool isMedicalLeader    = caller.Roles.Any(r => MedicalLeadershipRoleIds.Contains(r.Id));

        bool allowed =
            isSeniorManagement ||
            isSeniorModerator ||
            (subName == "police"    && isPoliceLeader) ||
            (subName == "ambulance" && isMedicalLeader);

        if (!allowed)
        {
            await Reply(command, "❌ You are not allowed to use this subcommand.");
            return;
        }

        // cid value
        var cid = sub.Options?.FirstOrDefault()?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(cid))
        {
            await Reply(command, $"❌ Please provide a valid CID, e.g. `/playtime {subName} JZE26242`");
            return;
        }

        // Build query per subcommand
        string column  = subName == "police" ? "police_playtime" : "ambulance_playtime";
        string job     = subName; // "police" or "ambulance"
        string emoji   = subName == "police" ? "👮" : "🚑";
        string title   = subName == "police" ? "Police Playtime" : "Ambulance Playtime";
        string sql = $@"
            SELECT {column}
            FROM player_jobs
            WHERE cid = @cid AND job = @job
            ORDER BY {column} DESC
            LIMIT 1;";

        try
        {
            int? minutes = null;

            using (var conn = new MySqlConnection(ConnectionString))
            {
                await conn.OpenAsync();

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@cid", cid);
                    cmd.Parameters.AddWithValue("@job", job);

                    var obj = await cmd.ExecuteScalarAsync();
                    if (obj != null && obj != DBNull.Value)
                    {
                        minutes = Convert.ToInt32(obj);
                    }
                }
            }

            if (minutes is null)
            {
                await Reply(command, $"ℹ️ No {subName} playtime found for `cid: {Escape(cid)}`.");
                return;
            }

            var (hours, mins) = (minutes.Value / 60, minutes.Value % 60);

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription($"{emoji} Results for `cid: {Escape(cid)}`")
                .AddField("Minutes", minutes.Value.ToString("N0"), true)
                .AddField("Approx. Hours", $"{hours}h {mins}m", true)
                .WithColor(subName == "police" ? new Color(0x3B, 0x82, 0xF6) : new Color(0x22, 0xC5, 0x5E)) // blue/green-ish
                .WithFooter($"Requested by {caller.DisplayName}")
                .WithCurrentTimestamp()
                .Build();

            await command.ModifyOriginalResponseAsync(m =>
            {
                m.Content = string.Empty;
                m.Embed = embed;
            });
        }
        catch (Exception)
        {
            await Reply(command, "❌ Database error while fetching playtime.");
        }
    }

    private static string Escape(string s) => s.Replace("`", "ˋ");

    private static Task Reply(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
