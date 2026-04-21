using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class SpcStashClearCommand : ISlashCommand
{
    public string Name => "spcstashclear";
    public string Description => "Clears metadata from SPC stashes (spc-stash & spc-stash2)";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private const ulong SeniorManagementRoleId = 1393590761953558608;
    private const ulong LogChannelId           = 1440488262836813874;

    // ✅ Added Police Leadership roles
    private static readonly HashSet<ulong> PoliceLeadershipRoleIds = new()
    {
        1394457219298492527,
        1394458024503935006,
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (command.User is not SocketGuildUser caller)
        {
            await Reply(command, "❌ Must be used in a server.");
            return;
        }

        // ✅ UPDATED PERMISSION CHECK
        bool allowed =
            caller.Roles.Any(r => r.Id == SeniorManagementRoleId) ||
            caller.Roles.Any(r => PoliceLeadershipRoleIds.Contains(r.Id));

        if (!allowed)
        {
            await Reply(command, "❌ You are not authorised to use this command.");
            return;
        }

        try
        {
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();

            using var cmd = new MySqlCommand(
                @"UPDATE ox_inventory
                  SET data = NULL
                  WHERE name IN ('spc-stash', 'spc-stash2')",
                conn);

            int affected = await cmd.ExecuteNonQueryAsync();

            var embed = new EmbedBuilder()
                .WithTitle("🧹 Superintendent+ Evidence Locker Cleared")
                .WithColor(new Color(59, 130, 246))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .AddField("Affected Rows", affected, true)
                .AddField("Stashes", "`spc-stash`, `spc-stash2`", false)
                .AddField("Cleared By", caller.DisplayName, true)
                .WithFooter("Evidence Locker");

            var logChannel = (command.Channel as SocketGuildChannel)?
                .Guild
                .GetTextChannel(LogChannelId);

            if (logChannel != null)
                await logChannel.SendMessageAsync(embed: embed.Build());

            await Reply(command, $"✅ Cleared metadata for `{affected}` stash entries.");
        }
        catch (Exception ex)
        {
            await Reply(command, "❌ Database error while clearing stash.");
            Console.WriteLine(ex);
        }
    }

    private static Task Reply(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
