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
    public string Description => "Blacklist a citizen from the police job";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // Allowed roles
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
            await Reply(command, "❌ You are not authorised to police-blacklist.");
            return;
        }

        var options = command.Data.Options.ToList();
        if (options.Count < 2)
        {
            await Reply(command,
                "❌ Usage: `/policeblacklist <CID> <days|PERM> <grade>`");
            return;
        }

        string citizenid = options[0].Value?.ToString()?.Trim();
        string duration  = options[1].Value?.ToString()?.Trim().ToUpperInvariant();

        // NEW: optional grade
        int? maxGrade = null;
        if (options.Count >= 3 && options[2].Value != null)
        {
            if (!int.TryParse(options[2].Value.ToString(), out int parsedGrade) || parsedGrade < 0)
            {
                await Reply(command, "❌ Grade must be a number ≥ 0.");
                return;
            }
            maxGrade = parsedGrade;
        }

        if (string.IsNullOrWhiteSpace(citizenid))
        {
            await Reply(command, "❌ Invalid citizen ID.");
            return;
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

            // Prevent duplicate active blacklist
            using (var check = new MySqlCommand(
                @"SELECT id FROM police_blacklist
                  WHERE citizenid = @cid
                  AND (expires_at IS NULL OR expires_at > NOW())
                  LIMIT 1", conn))
            {
                check.Parameters.AddWithValue("@cid", citizenid);
                var exists = await check.ExecuteScalarAsync();
                if (exists != null)
                {
                    await Reply(command, $"⚠️ `{citizenid}` is already police-blacklisted.");
                    return;
                }
            }

            // NEW: max_grade column added
            using (var insert = new MySqlCommand(
                @"INSERT INTO police_blacklist
                  (citizenid, expires_at, banned_by, reason, max_grade)
                  VALUES (@cid, @expires, @by, @reason, @maxGrade)", conn))
            {
                insert.Parameters.AddWithValue("@cid", citizenid);
                insert.Parameters.AddWithValue("@expires",
                    expiresAt.HasValue ? expiresAt : DBNull.Value);
                insert.Parameters.AddWithValue("@by", caller.DisplayName);
                insert.Parameters.AddWithValue("@reason",
                    "Police blacklist issued via Discord.");
                insert.Parameters.AddWithValue("@maxGrade",
                    maxGrade.HasValue ? maxGrade : DBNull.Value);

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
        }
        catch (Exception)
        {
            await Reply(command, "❌ Database error while creating blacklist.");
        }
    }

    private static Task Reply(SocketSlashCommand cmd, string text) =>
        cmd.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}
