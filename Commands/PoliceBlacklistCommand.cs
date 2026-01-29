using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class PoliceBlacklistCommand : ISlashCommand
{
    public string Name => "policeblacklist";
    public string Description => "Manage police blacklist";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private const ulong SeniorManagementRoleId = 1393590761953558608;
    private const ulong SeniorModeratorRoleId  = 1393638449709584434;

    private static readonly HashSet<ulong> PoliceLeadershipRoleIds = new()
    {
        1394649657644290078,
        1394458024503935006,
        1394457219298492527,
    };

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
            caller.Roles.Any(r => PoliceLeadershipRoleIds.Contains(r.Id));

        if (!allowed)
        {
            await Reply(command, "❌ You are not authorised to manage police blacklists.");
            return;
        }

        // 🔑 SUBCOMMAND HANDLING
        var sub = command.Data.Options.First();
        var subName = sub.Name;
        var opts = sub.Options.ToList();

        // =========================================================
        // /policeblacklist add <cid> <days|PERM> <grade?>
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
                    @"SELECT id FROM police_blacklist
                      WHERE citizenid = @cid
                      AND (expires_at IS NULL OR expires_at > NOW())
                      LIMIT 1", conn))
                {
                    check.Parameters.AddWithValue("@cid", citizenid);
                    if (await check.ExecuteScalarAsync() != null)
                    {
                        await Reply(command, $"⚠️ `{citizenid}` is already police-blacklisted.");
                        return;
                    }
                }

                using (var insert = new MySqlCommand(
                    @"INSERT INTO police_blacklist
                      (citizenid, expires_at, banned_by, reason, max_grade)
                      VALUES (@cid, @expires, @by, @reason, @grade)", conn))
                {
                    insert.Parameters.AddWithValue("@cid", citizenid);
                    insert.Parameters.AddWithValue("@expires", expiresAt ?? (object)DBNull.Value);
                    insert.Parameters.AddWithValue("@by", caller.DisplayName);
                    insert.Parameters.AddWithValue("@reason", "Police blacklist issued via Discord.");
                    insert.Parameters.AddWithValue("@grade", maxGrade ?? (object)DBNull.Value);

                    await insert.ExecuteNonQueryAsync();
                }

                string expiryText = expiresAt.HasValue
                    ? $"until <t:{((DateTimeOffset)expiresAt.Value).ToUnixTimeSeconds()}:f>"
                    : "permanently";

                string gradeText = maxGrade.HasValue
                    ? $" (max grade **{maxGrade.Value}**)"
                    : " (full ban)";

                await Reply(command,
                    $"✅ `{citizenid}` has been **police-blacklisted** {expiryText}{gradeText}.");
                return;
            }
            catch
            {
                await Reply(command, "❌ Database error while creating blacklist.");
                return;
            }
        }

        // =========================================================
        // /policeblacklist remove <cid>
        // =========================================================
        if (subName == "remove")
        {
            string citizenid = opts[0].Value.ToString().Trim();

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                await conn.OpenAsync();

                using var del = new MySqlCommand(
                    "DELETE FROM police_blacklist WHERE citizenid = @cid",
                    conn);

                del.Parameters.AddWithValue("@cid", citizenid);
                int affected = await del.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    await Reply(command, $"⚠️ `{citizenid}` is not police-blacklisted.");
                    return;
                }

                await Reply(command,
                    $"✅ `{citizenid}` has been removed from the police blacklist.");
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

    private static Task Reply(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
