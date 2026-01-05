using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

public class GameStatsCommand : ISlashCommand
{
    public string Name => "gamestats";
    public string Description => "Shows server-wide game statistics (Senior Management only)";

    private const ulong SeniorManagementRoleId = 1393590761953558608;

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        if (command.User is not SocketGuildUser caller)
        {
            await Deny(command, "❌ This command must be used in a server.");
            return;
        }

        if (!caller.Roles.Any(r => r.Id == SeniorManagementRoleId))
        {
            await Deny(command, "❌ Only **Senior Management** can use this command.");
            return;
        }

        try
        {
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();

            async Task<string> Top3Async(string sql, string suffix)
            {
                using var cmd = new MySqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                var sb = new StringBuilder();
                int rank = 1;

                // 🔑 Resolve ordinals ONCE
                int nameOrd = reader.GetOrdinal("character_name");
                int discOrd = reader.GetOrdinal("discordid");
                int valOrd  = reader.GetOrdinal("val");

                while (await reader.ReadAsync())
                {
                    string characterName = reader.IsDBNull(nameOrd)
                        ? "Unknown"
                        : reader.GetString(nameOrd);

                    string discordId = reader.IsDBNull(discOrd)
                        ? "0"
                        : reader.GetInt64(discOrd).ToString();

                    long value = reader.IsDBNull(valOrd)
                        ? 0
                        : reader.GetInt64(valOrd);

                    sb.AppendLine(
                        $"**{rank}.** <@{discordId}> ({characterName}) — **{FormatValue(value, suffix)}**"
                    );

                    rank++;
                }

                return sb.Length > 0 ? sb.ToString() : "_No data_";
            }

            string richest = await Top3Async(@"
                SELECT character_name, discordid, totalmoney AS val
                FROM datadiscord
                ORDER BY totalmoney DESC
                LIMIT 3", "£");

            string poorest = await Top3Async(@"
                SELECT character_name, discordid, totalmoney AS val
                FROM datadiscord
                ORDER BY totalmoney ASC
                LIMIT 3", "£");

            string jailed = await Top3Async(@"
                SELECT character_name, discordid, totaljailtime AS val
                FROM datadiscord
                ORDER BY totaljailtime DESC
                LIMIT 3", " mins");

            string fined = await Top3Async(@"
                SELECT character_name, discordid, totalfines AS val
                FROM datadiscord
                ORDER BY totalfines DESC
                LIMIT 3", "£");

            string vehicles = await Top3Async(@"
                SELECT character_name, discordid, totalvehicles AS val
                FROM datadiscord
                ORDER BY totalvehicles DESC
                LIMIT 3", " vehicles");

            string clean = await Top3Async(@"
                SELECT character_name, discordid, totalmoney AS val
                FROM datadiscord
                WHERE totaljailtime = 0 AND totalfines = 0
                ORDER BY totalmoney DESC
                LIMIT 3", "£");

            var embed = new EmbedBuilder()
                .WithTitle("📊 Server Game Statistics")
                .WithColor(new Color(0xF5, 0x9E, 0x0B))
                .AddField("💰 Walking Economy", richest, false)
                .AddField("🪙 Card Declined", poorest, false)
                .AddField("🚔 State Property", jailed, false)
                .AddField("💸 Radar Magnet", fined, false)
                .AddField("🚗 Car Hoarder Disorder", vehicles, false)
                .AddField("😇 Suspiciously Clean", clean, false)
                .WithFooter($"Requested by {caller.DisplayName}")
                .WithCurrentTimestamp()
                .Build();

            await command.ModifyOriginalResponseAsync(m =>
            {
                m.Content = string.Empty;
                m.Embed = embed;
            });
        }
        catch
        {
            await Deny(command, "❌ Database error while fetching game statistics.");
        }
    }

    private static string FormatValue(long value, string suffix)
    {
        if (suffix == "£")
            return $"£{value:N0}";

        return $"{value:N0}{suffix}";
    }

    private static Task Deny(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
