using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class TFHSBlacklistCommand : ISlashCommand
{
    public string Name => "tfhsblacklist";
    public string Description => "Manage TFHS blacklist";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private const ulong SeniorManagementRoleId = 1393590761953558608;
    private const ulong SeniorModeratorRoleId  = 1393638449709584434;
    private const ulong LogChannelId           = 1404782091199053847;

    private static readonly HashSet<ulong> TFHSLeadershipRoleIds = new()
    {
        1394460400988454953,
        1394460689338208296,
        1398308435795251302,
    };

    // =========================================================
    // DATA MODELS
    // =========================================================
    private sealed class DiscordData
    {
        public string CitizenId { get; init; }
        public string CharacterName { get; init; }
        public ulong? DiscordId { get; init; }
    }

    // =========================================================
    // MAIN COMMAND
    // =========================================================
    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (command.User is not SocketGuildUser caller)
        {
            await Reply(command, "❌ Must be used in a server.");
            return;
        }

        bool allowed =
            caller.Roles.Any(r => r.Id == SeniorManagementRoleId) ||
            caller.Roles.Any(r => r.Id == SeniorModeratorRoleId) ||
            caller.Roles.Any(r => TFHSLeadershipRoleIds.Contains(r.Id));

        if (!allowed)
        {
            await Reply(command, "❌ You are not authorised to manage TFHS blacklists.");
            return;
        }

        var sub = command.Data.Options.First();
        var subName = sub.Name;
        var opts = sub.Options.ToList();

        // =========================================================
        // /tfhsblacklist add <cid> <days|PERM> <grade?>
        // =========================================================
        if (subName == "add")
        {
            string citizenid = opts[0].Value.ToString().Trim();
            string duration  = opts[1].Value.ToString().Trim().ToUpperInvariant();

            int? maxGrade = null;
            if (opts.Count >= 3 && opts[2].Value != null)
            {
                if (!int.TryParse(opts[2].Value.ToString(), out int g) || g < 0)
                {
                    await Reply(command, "❌ Grade must be a number ≥ 0.");
                    return;
                }
                maxGrade = g;
            }

            DateTime? expiresAt = null;
            if (duration != "PERM")
            {
                if (!int.TryParse(duration, out int days) || days <= 0)
                {
                    await Reply(command, "❌ Duration must be a number of days or `PERM`.");
                    return;
                }
                expiresAt = DateTime.UtcNow.AddDays(days);
            }

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                await conn.OpenAsync();

                using (var check = new MySqlCommand(
                    @"SELECT id FROM tfhs_blacklist
                      WHERE citizenid = @cid
                      AND (expires_at IS NULL OR expires_at > NOW())
                      LIMIT 1", conn))
                {
                    check.Parameters.AddWithValue("@cid", citizenid);
                    if (await check.ExecuteScalarAsync() != null)
                    {
                        await Reply(command, $"⚠️ `{citizenid}` is already TFHS-blacklisted.");
                        return;
                    }
                }

                using (var insert = new MySqlCommand(
                    @"INSERT INTO tfhs_blacklist
                      (citizenid, expires_at, banned_by, reason, max_grade)
                      VALUES (@cid, @expires, @by, @reason, @grade)", conn))
                {
                    insert.Parameters.AddWithValue("@cid", citizenid);
                    insert.Parameters.AddWithValue("@expires", expiresAt ?? (object)DBNull.Value);
                    insert.Parameters.AddWithValue("@by", caller.DisplayName);
                    insert.Parameters.AddWithValue("@reason", "TFHS blacklist issued via Discord.");
                    insert.Parameters.AddWithValue("@grade", maxGrade ?? (object)DBNull.Value);

                    await insert.ExecuteNonQueryAsync();
                }

                var data = await GetDiscordDataAsync(conn, citizenid);

                var embed = new EmbedBuilder()
                    .WithTitle("🚑 TFHS Blacklist Issued")
                    .WithColor(new Color(220, 38, 38))
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .AddField("CitizenID", $"`{citizenid}`", true)
                    .AddField("Character Name", data?.CharacterName ?? "Unknown", true)
                    .AddField("Discord",
                        data?.DiscordId.HasValue == true
                            ? $"<@{data.DiscordId}> (`{data.DiscordId}`)"
                            : "Not linked",
                        false)
                    .AddField("Duration",
                        expiresAt.HasValue
                            ? $"Until <t:{((DateTimeOffset)expiresAt.Value).ToUnixTimeSeconds()}:f>"
                            : "Permanent",
                        true)
                    .AddField("Restriction",
                        maxGrade.HasValue
                            ? $"Max grade **{maxGrade.Value}**"
                            : "Full TFHS ban",
                        true)
                    .AddField("Issued By", caller.DisplayName, true)
                    .WithFooter("TFHS Blacklist System");

                var logChannel = (command.Channel as SocketGuildChannel)?
                    .Guild
                    .GetTextChannel(LogChannelId);

                if (logChannel != null)
                {
                    await logChannel.SendMessageAsync(embed: embed.Build());
                }

                await Reply(command,
                    $"✅ `{citizenid}` has been **TFHS-blacklisted** successfully.");
                return;
            }
            catch
            {
                await Reply(command, "❌ Database error while creating blacklist.");
                return;
            }
        }

        // =========================================================
        // /tfhsblacklist remove <cid>
        // =========================================================
        if (subName == "remove")
        {
            string citizenid = opts[0].Value.ToString().Trim();

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                await conn.OpenAsync();

                using var del = new MySqlCommand(
                    "DELETE FROM tfhs_blacklist WHERE citizenid = @cid",
                    conn);

                del.Parameters.AddWithValue("@cid", citizenid);
                int affected = await del.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    await Reply(command, $"⚠️ `{citizenid}` is not TFHS-blacklisted.");
                    return;
                }

                var data = await GetDiscordDataAsync(conn, citizenid);

                var embed = new EmbedBuilder()
                    .WithTitle("✅ TFHS Blacklist Removed")
                    .WithColor(new Color(34, 197, 94))
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .AddField("CitizenID", $"`{citizenid}`", true)
                    .AddField("Character Name", data?.CharacterName ?? "Unknown", true)
                    .AddField("Discord",
                        data?.DiscordId.HasValue == true
                            ? $"<@{data.DiscordId}> (`{data.DiscordId}`)"
                            : "Not linked",
                        false)
                    .AddField("Removed By", caller.DisplayName, true)
                    .WithFooter("TFHS Blacklist System");

                var logChannel = (command.Channel as SocketGuildChannel)?
                    .Guild
                    .GetTextChannel(LogChannelId);

                if (logChannel != null)
                {
                    await logChannel.SendMessageAsync(embed: embed.Build());
                }

                await Reply(command,
                    $"✅ `{citizenid}` has been removed from the TFHS blacklist.");
                return;
            }
            catch
            {
                await Reply(command, "❌ Database error while removing blacklist.");
                return;
            }
        }

        await Reply(command, "❌ Unknown subcommand.");
    }

    // =========================================================
    // HELPERS
    // =========================================================
    private static async Task<DiscordData?> GetDiscordDataAsync(
        MySqlConnection conn, string citizenid)
    {
        using var cmd = new MySqlCommand(
            @"SELECT citizenid, character_name, discordid
              FROM datadiscord
              WHERE citizenid = @cid
              LIMIT 1", conn);

        cmd.Parameters.AddWithValue("@cid", citizenid);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        ulong? discordId = null;
        if (reader["discordid"] != DBNull.Value &&
            ulong.TryParse(reader["discordid"].ToString(), out var did))
        {
            discordId = did;
        }

        return new DiscordData
        {
            CitizenId = reader["citizenid"].ToString(),
            CharacterName = reader["character_name"].ToString(),
            DiscordId = discordId
        };
    }

    private static Task Reply(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}