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

            // ----------------------------
            // Helper: check if a guild user is Senior Management
            // ----------------------------
            bool IsSeniorManagement(SocketGuildUser user) =>
                user.Roles.Any(r => r.Id == SeniorManagementRoleId);

            // ----------------------------
            // Top N formatter for normal stats
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
                    if (rank > limit) break;

                    string characterName = reader.IsDBNull(nameOrd) ? "Unknown" : reader.GetString(nameOrd);
                    if (reader.IsDBNull(discOrd)) continue;
                    ulong discordId = (ulong)reader.GetInt64(discOrd); // ✅ cast to ulong

                    var guildUser = caller.Guild.GetUser(discordId);
                    if (guildUser == null || IsSeniorManagement(guildUser)) 
                        continue; // skip if not in guild OR is Senior Management

                    long value = reader.IsDBNull(valOrd) ? 0 : reader.GetInt64(valOrd);

                    sb.AppendLine($"**{rank}.** <@{discordId}> ({characterName}) — **{FormatValue(value, suffix)}**");
                    rank++;
                }

                return sb.Length > 0 ? sb.ToString() : "_No data_";
            }

            // ----------------------------
            // Special formatter for jailed time (hours + minutes)
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
                    if (rank > 3) break;

                    string characterName = reader.IsDBNull(nameOrd) ? "Unknown" : reader.GetString(nameOrd);
                    if (reader.IsDBNull(discOrd)) continue;
                    ulong discordId = (ulong)reader.GetInt64(discOrd); // ✅ cast to ulong

                    var guildUser = caller.Guild.GetUser(discordId);
                    if (guildUser == null || IsSeniorManagement(guildUser)) 
                        continue; // skip if not in guild OR is Senior Management

                    long minutes = reader.IsDBNull(valOrd) ? 0 : reader.GetInt64(valOrd);
                    long hours = minutes / 60;
                    long mins  = minutes % 60;

                    sb.AppendLine($"**{rank}.** <@{discordId}> ({characterName}) — **{hours}h {mins}m**");
                    rank++;
                }

                return sb.Length > 0 ? sb.ToString() : "_No data_";
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
            // Suspiciously Clean — top 10 names only, must be in guild, must NOT be Senior Management
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
                    string characterName = reader.IsDBNull(nameOrd) ? "Unknown" : reader.GetString(nameOrd);
                    if (reader.IsDBNull(discOrd)) continue;
                    ulong discordId = (ulong)reader.GetInt64(discOrd); // ✅ cast to ulong

                    var guildUser = caller.Guild.GetUser(discordId);
                    if (guildUser == null || IsSeniorManagement(guildUser))
                        continue; // skip if not in guild OR is Senior Management

                    sb.AppendLine($"**{rank}.** <@{discordId}> ({characterName})");
                    rank++;
                }

                clean = sb.Length > 0 ? sb.ToString() : "_No data_";
            }

            // ----------------------------
            // Build embed
            // ----------------------------
            var embed = new EmbedBuilder()
                .WithTitle("📊 Server Game Statistics")
                .WithColor(new Color(0xF5, 0x9E, 0x0B))
                .AddField("💰 Walking Economy", richest, false)
                .AddField("🪙 Card Declined", poorest, false)
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

        return $"{value:N0}{suffix}";
    }

    private static Task Deny(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
