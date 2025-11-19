using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using MySqlConnector; // MySqlConnector NuGet package

public class GameModCommands : ISlashCommand
{
    public string Name => "game";  // ✅ stays as /game
    public string Description => "Game moderation commands";

    // ✅ Roles allowed to use /game general (keeps current roles)
    private static readonly ulong[] AllowedRoleIds = new ulong[]
    {
        1393729574537396355UL, // Game Moderator
        1393590761953558608UL  // SM
    };

    // ✅ Roles allowed to use deletecharacter specifically (Senior Mod + SM)
    private static readonly ulong[] DeleteAllowedRoleIds = new ulong[]
    {
        1393638449709584434UL, // Senior Moderator
        1393590761953558608UL  // SM
    };

    // ✅ Direct database connection (no RCON)
    private const string MYSQL_CONN =
        "Server=nw26472-001.eu.clouddb.ovh.net;" +
        "Port=35666;" +
        "Database=thefirm_qbcore;" +
        "User ID=thefirmprod;" +
        "Password=edr6BYZqmq7eud0mwm;" +
        "Character Set=utf8mb4;" +
        "SslMode=Required;";

    // ✅ Build: /game returnvehicle and /game deletecharacter
    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("returnvehicle")
                .WithDescription("Return a vehicle by plate")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("plate", ApplicationCommandOptionType.String, "Vehicle plate, e.g. AB12 ABC", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("deletecharacter")
                .WithDescription("Delete a character by citizen id (Senior Mod + SM only)")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("citizenid", ApplicationCommandOptionType.String, "Citizen ID, e.g. MUF58516", isRequired: true))
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.GuildId == null)
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        var user = command.User as SocketGuildUser;
        if (user == null || !user.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await command.RespondAsync("❌ You do not have permission to use this.", ephemeral: true);
            return;
        }

        await command.DeferAsync(ephemeral: true);

        var sub = command.Data.Options.First().Name.ToLower();
        if (sub == "returnvehicle")
            await HandleReturnVehicle(command, command.Data.Options.First().Options);
        else if (sub == "deletecharacter")
            await HandleDeleteCharacter(command, command.Data.Options.First().Options);
        else
            await command.FollowupAsync("Unknown subcommand.", ephemeral: true);
    }

    // ✅ Handles /game returnvehicle plate: XXX
    private async Task HandleReturnVehicle(SocketSlashCommand command, System.Collections.Generic.IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        var plate = options.First(o => o.Name == "plate").Value?.ToString()?.Trim() ?? "";

        if (!Regex.IsMatch(plate, @"^[A-Za-z0-9 ]{1,12}$"))
        {
            await command.FollowupAsync("❌ Invalid plate format.", ephemeral: true);
            return;
        }

        try
        {
            int affected;
            using (var conn = new MySqlConnection(MYSQL_CONN))
            {
                await conn.OpenAsync();

                var sql = @"
                    UPDATE player_vehicles
                    SET garage_id = @garageId,
                        in_garage = 1,
                        impound = 0
                    WHERE UPPER(plate) = UPPER(@plate)
                    LIMIT 1;
                ";

                using var cmd = new MySqlCommand(sql, conn);
                // 🔧 If your schema uses a numeric ID, change the value type accordingly.
                cmd.Parameters.AddWithValue("@garageId", "Legion Square");
                cmd.Parameters.AddWithValue("@plate", plate);

                affected = await cmd.ExecuteNonQueryAsync();
            }

            if (affected == 0)
            {
                await command.FollowupAsync($"⚠️ No vehicle found with plate `{plate}`.", ephemeral: true);
            }
            else
            {
                await command.FollowupAsync($"✅ Vehicle `{plate}` has been returned to **Legion Square**.", ephemeral: true);

                // 🔻 NEW: log to channel 1394451583709745273
                try
                {
                    var guild = (command.User as SocketGuildUser)?.Guild;
                    var logChannel = guild?.GetTextChannel(1394451583709745273UL);
                    if (logChannel != null)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("🚗 Vehicle Returned")
                            .WithDescription(
                                $"**VRN:** `{plate}`\n" +
                                $"**Initiated by:** {command.User.Mention}")
                            .WithColor(Color.Green)
                            .WithFooter(f => f.Text = "Command: /game returnvehicle")
                            .WithTimestamp(DateTimeOffset.UtcNow)
                            .Build();

                        await logChannel.SendMessageAsync(embed: embed);
                    }
                }
                catch { /* ignore logging errors */ }
            }
        }
        catch (Exception ex)
        {
            await command.FollowupAsync($"❌ Database error: `{ex.Message}`", ephemeral: true);
        }
    }

    // make sure to add: using System.Text.Json;
    private async Task HandleDeleteCharacter(SocketSlashCommand command,
        System.Collections.Generic.IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        var invoker = command.User as SocketGuildUser;
        if (invoker == null || !invoker.Roles.Any(r => DeleteAllowedRoleIds.Contains(r.Id)))
        {
            await command.FollowupAsync("❌ You do not have permission to use this subcommand. (Senior Mod or SM only)",
                ephemeral: true);
            return;
        }

        var citizenId = options.First(o => o.Name == "citizenid").Value?.ToString()?.Trim() ?? "";

        if (!Regex.IsMatch(citizenId, @"^[A-Za-z0-9]{3,16}$"))
        {
            await command.FollowupAsync("❌ Invalid citizen ID format.", ephemeral: true);
            return;
        }

        string playerName = null;
        string charinfoJson = null;
        string characterFullName = null; // firstname + lastname from charinfo
        int affected = 0;

        try
        {
            using (var conn = new MySqlConnection(MYSQL_CONN))
            {
                await conn.OpenAsync();

                // SELECT both name and charinfo (charinfo may be a JSON string)
                var getSql = @"
                SELECT name, charinfo
                FROM players
                WHERE UPPER(citizenid) = UPPER(@cid)
                LIMIT 1;
            ";

                using (var getCmd = new MySqlCommand(getSql, conn))
                {
                    getCmd.Parameters.AddWithValue("@cid", citizenId);
                    using var reader = await getCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        // read name column (player name)
                        playerName = reader["name"] == DBNull.Value ? null : reader["name"].ToString();

                        // read charinfo column (could be JSON stored as text)
                        charinfoJson = reader["charinfo"] == DBNull.Value ? null : reader["charinfo"].ToString();
                    }
                    else
                    {
                        await command.FollowupAsync($"⚠️ No player found with citizen id `{citizenId}`.",
                            ephemeral: true);
                        return;
                    }
                }

                // parse charinfo JSON safely to extract firstname + lastname (if present)
                if (!string.IsNullOrWhiteSpace(charinfoJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(charinfoJson);
                        var root = doc.RootElement;

                        string first = null, last = null;
                        if (root.TryGetProperty("firstname", out var firstElem) &&
                            firstElem.ValueKind == JsonValueKind.String)
                            first = firstElem.GetString();

                        if (root.TryGetProperty("lastname", out var lastElem) &&
                            lastElem.ValueKind == JsonValueKind.String)
                            last = lastElem.GetString();

                        if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                            characterFullName = $"{first ?? ""} {last ?? ""}".Trim();
                    }
                    catch
                    {
                        // ignore JSON parse errors — characterFullName stays null
                    }
                }

                // Proceed to delete (after fetching the names)
                var deleteSql = @"
                DELETE FROM players
                WHERE UPPER(citizenid) = UPPER(@cid)
                LIMIT 1;
            ";

                using (var delCmd = new MySqlCommand(deleteSql, conn))
                {
                    delCmd.Parameters.AddWithValue("@cid", citizenId);
                    affected = await delCmd.ExecuteNonQueryAsync();
                }
            }

            if (affected == 0)
            {
                await command.FollowupAsync(
                    $"⚠️ Character/player `{playerName ?? characterFullName ?? citizenId}` was not deleted (not found or already removed).",
                    ephemeral: true);
                return;
            }

            // reply to moderator
            var replyText = $"🗑️ Deleted **player** `{playerName ?? "N/A"}` (`{citizenId}`).";
            if (!string.IsNullOrWhiteSpace(characterFullName))
                replyText += $"\n**Character:** {characterFullName}";

            await command.FollowupAsync(replyText, ephemeral: true);

            // log to staff channel
            try
            {
                var guild = (command.User as SocketGuildUser)?.Guild;
                var logChannel = guild?.GetTextChannel(1394451583709745273UL);
                if (logChannel != null)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🗑️ Player Deleted")
                        .AddField("Citizen ID", citizenId, true)
                        .AddField("Player Name", playerName ?? "N/A", true)
                        .AddField("Character Name", characterFullName ?? "N/A", true)
                        .AddField("Deleted By", command.User.Mention, false)
                        .WithColor(Color.DarkRed)
                        .WithFooter(f => f.Text = "Command: /game deletecharacter")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await logChannel.SendMessageAsync(embed: embed);
                }
            }
            catch
            {
                /* ignore logging errors */
            }
        }
        catch (Exception ex)
        {
            await command.FollowupAsync($"❌ Database error: `{ex.Message}`", ephemeral: true);
        }
    }
}
