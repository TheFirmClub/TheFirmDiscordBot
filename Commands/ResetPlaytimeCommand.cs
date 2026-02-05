// File: ResetPlaytimeCommand.cs
using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ResetPlaytimeCommand : ISlashCommand
{
    public string Name => "resetplaytime";
    public string Description => "Reset police or ambulance playtime by CID";

    // --- DB ---
    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // --- Role gates ---
    private const ulong SeniorManagementRoleId = 1393590761953558608;
    private const ulong SeniorModeratorRoleId = 1393638449709584434;

    private static readonly HashSet<ulong> PoliceLeadershipRoleIds = new()
    {
        1394459419156418730, // Response Inspector
        1394650413361533009, // Roads Inspector
        1394465316817473646, // TFU Inspector
        1394649657644290078, // Chief Inspector
        1394458024503935006, // Superintendent
        1394457219298492527, // Commissioner
    };

    private static readonly HashSet<ulong> MedicalLeadershipRoleIds = new()
    {
        1398313269998911519, // Team Manager
        1398308435795251302, // COO
        1394460689338208296, // Medical Director
        1394460400988454953, // CMO
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (command.User is not SocketGuildUser caller ||
            (command.Channel as SocketGuildChannel)?.Guild == null)
        {
            await Reply(command, "❌ This command must be used in a server.");
            return;
        }

        if (command.Data.Options is null || command.Data.Options.Count == 0)
        {
            await Reply(command, "❌ Usage: `/resetplaytime police <CID>` or `/resetplaytime ambulance <CID>`");
            return;
        }

        var sub = command.Data.Options.First();
        var subName = sub.Name?.ToLowerInvariant();

        if (subName != "police" && subName != "ambulance")
        {
            await Reply(command, "❌ Unknown subcommand. Use `police` or `ambulance`.");
            return;
        }

        // --- Permission check (MATCHES PlaytimeCommand.cs) ---
        bool isSeniorManagement = caller.Roles.Any(r => r.Id == SeniorManagementRoleId);
        bool isSeniorModerator = caller.Roles.Any(r => r.Id == SeniorModeratorRoleId);
        bool isPoliceLeader = caller.Roles.Any(r => PoliceLeadershipRoleIds.Contains(r.Id));
        bool isMedicalLeader = caller.Roles.Any(r => MedicalLeadershipRoleIds.Contains(r.Id));

        bool allowed =
            isSeniorManagement ||
            isSeniorModerator ||
            (subName == "police" && isPoliceLeader) ||
            (subName == "ambulance" && isMedicalLeader);

        if (!allowed)
        {
            await Reply(command, "❌ You are not allowed to use this subcommand.");
            return;
        }

        var cid = sub.Options?.FirstOrDefault()?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(cid))
        {
            await Reply(command, $"❌ Please provide a valid CID.");
            return;
        }

        string column = subName == "police" ? "police_playtime" : "ambulance_playtime";
        string job = subName;
        string emoji = subName == "police" ? "👮" : "🚑";
        string title = subName == "police" ? "Police Playtime Reset" : "Ambulance Playtime Reset";

        string sql = $@"
            UPDATE player_jobs
            SET {column} = 0
            WHERE cid = @cid AND job = @job;";

        try
        {
            int rows;

            using (var conn = new MySqlConnection(ConnectionString))
            {
                await conn.OpenAsync();

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@cid", cid);
                    cmd.Parameters.AddWithValue("@job", job);

                    rows = await cmd.ExecuteNonQueryAsync();
                }
            }

            if (rows == 0)
            {
                await Reply(command, $"ℹ️ No {subName} playtime found for `cid: {Escape(cid)}`.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription($"{emoji} Playtime has been successfully reset")
                .AddField("CID", Escape(cid), true)
                .AddField("Job", subName, true)
                .WithColor(new Color(0xEF, 0x44, 0x44))
                .WithFooter($"Reset by {caller.DisplayName}")
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
            await Reply(command, "❌ Database error while resetting playtime.");
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
