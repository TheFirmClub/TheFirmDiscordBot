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

            bool IsSeniorManagement(SocketGuildUser user) =>
                user.Roles.Any(r => r.Id == SeniorManagementRoleId);

            // ----------------------------
            // Top N formatter for normal stats (Richest, Poorest, Fined, Vehicles, Arrested)
            // ----------------------------
            async Task<string> TopNAsync(string sql, string suffix, int limit = 3)
            {
                using var cmd = new MySqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                var sb = new StringBuilder();
                int rank = 1;

                int nameOrd = reader.GetOrdinal("character_name");
                int discOrd = reader.GetOrdinal("discordid");
                int valOrd  = reader.GetOrdinal("val");

                while (await reader.ReadAsync())
                {
                    if (reader.IsDBNull(discOrd)) continue;
                    ulong discordId = (ulong)reader.GetInt64(discOrd);
                    if (discordId == 0) continue;

                    var guildUser = caller.Guild.GetUser(discordId);
                    if (guildUser == null) continue;
                    if (IsSeniorManagement(guildUser)) continue;

                    if (rank > limit) break;

                    string characterName = reader.IsDBNull(nameOrd) ? "Unknown" : reader.GetString(nameOrd);
                    long value = reader.IsDBNull(valOrd) ? 0 : reader.GetInt64(valOrd);

                    sb.AppendLine($"**{rank}.** <@{discordId}> ({characterName}) — {FormatValue(value, suffix)}");
                    rank++;
                }

                return sb.Length > 0 ? sb.ToString() : "_No data_";
            }

            // ----------------------------
            // Top 3 jailed time (seconds → days + hours + minutes)
            // ----------------------------
            async Task<string> Top3HoursAsync(string sql)
            {
                using var cmd = new MySqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                var sb = new StringBuilder();
                int rank = 1;

                int nameOrd = reader.GetOrdinal("character_name");
                int discOrd = reader.GetOrdinal("discordid");
                int valOrd  = reader.GetOrdinal("val");

                while (await reader.ReadAsync())
                {
                    if (reader.IsDBNull(discOrd)) continue;
                    ulong discordId = (ulong)reader.GetInt64(discOrd);
                    if (discordId == 0) continue;

                    var guildUser = caller.Guild.GetUser(discordId);
                    if (guildUser == null) continue;
                    if (IsSeniorManagement(guildUser)) continue;

                    if (rank > 3) break;

                    string characterName = reader.IsDBNull(nameOrd) ? "Unknown" : reader.GetString(nameOrd);
                    long seconds = reader.IsDBNull(valOrd) ? 0 : reader.GetInt64(valOrd);

                    long days  = seconds / 86400;
                    long hours = (seconds % 86400) / 3600;
                    long mins  = (seconds % 3600) / 60;

                    var timeStr = "";
                    if (days > 0) timeStr += $"{days}d ";
                    if (hours > 0 || days > 0) timeStr += $"{hours}h ";
                    timeStr += $"{mins}m";

                    sb.AppendLine($"**{rank}.** <@{discordId}> ({characterName}) — **{timeStr}**");
                    rank++;
                }

                return sb.Length > 0 ? sb.ToString() : "_No data_";
            }

            // ----------------------------
            // Top 10 Suspiciously Clean (names only)
            // ----------------------------
            string clean;
            using (var cmd = new MySqlCommand(@"
                SELECT character_name, discordid
                FROM datadiscord
                WHERE totaljailtime = 0 AND totalfines = 0 AND discordid IS NOT NULL AND discordid != 0
                ORDER BY character_name ASC
                LIMIT 50", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                var sb = new StringBuilder();
                int rank = 1;

                int nameOrd = reader.GetOrdinal("character_name");
                int discOrd = reader.GetOrdinal("discordid");

                while (await reader.ReadAsync() && rank <= 10)
                {
                    if (reader.IsDBNull(discOrd)) continue;
                    ulong discordId = (ulong)reader.GetInt64(discOrd);
                    if (discordId == 0) continue;

                    var guildUser = caller.Guild.GetUser(discordId);
                    if (guildUser == null || IsSeniorManagement(guildUser)) continue;

                    string characterName = reader.IsDBNull(nameOrd) ? "Unknown" : reader.GetString(nameOrd);

                    sb.AppendLine($"**{rank}.** <@{discordId}> ({characterName})");
                    rank++;
                }

                clean = sb.Length > 0 ? sb.ToString() : "_No data_";
            }

            // ----------------------------
            // Fetch all stats
            // ----------------------------
            string richest = await TopNAsync(@"
                SELECT character_name, discordid, totalmoney AS val
                FROM datadiscord
                ORDER BY totalmoney DESC", "£");

            string poorest = await TopNAsync(@"
                SELECT character_name, discordid, totalmoney AS val
                FROM datadiscord
                ORDER BY totalmoney ASC", "£");

            string arrested = await TopNAsync(@"
                SELECT character_name, discordid, totalarrested AS val
                FROM datadiscord
                ORDER BY totalarrested DESC", " times"); // <- "Arrested X times"

            string jailed = await Top3HoursAsync(@"
                SELECT character_name, discordid, totaljailtime AS val
                FROM datadiscord
                ORDER BY totaljailtime DESC");

            string fined = await TopNAsync(@"
                SELECT character_name, discordid, totalfines AS val
                FROM datadiscord
                ORDER BY totalfines DESC", "£");

            string vehicles = await TopNAsync(@"
                SELECT character_name, discordid, totalvehicles AS val
                FROM datadiscord
                ORDER BY totalvehicles DESC", " vehicles");

            // ----------------------------
            // Build embed
            // ----------------------------
            var embed = new EmbedBuilder()
                .WithTitle("📊 Server Game Statistics")
                .WithColor(new Color(0xF5, 0x9E, 0x0B))
                .AddField("💰 Walking Economy", richest, false)
                .AddField("🪙 Card Declined", poorest, false)
                .AddField("✈️ Frequent Flyer", arrested, false) // now says "Arrested X times"
                .AddField("🚔 State Property", jailed, false)
                .AddField("💸 Radar Magnet", fined, false)
                .AddField("🚗 Car Hoarder Disorder", vehicles, false)
                .AddField("😇 Suspiciously Clean (Top 10)", clean, false)
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
        if (suffix == " times")
            return $"Arrested {value} times";

        return $"{value:N0}{suffix}";
    }

    private static Task Deny(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
