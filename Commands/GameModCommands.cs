using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using MySqlConnector; // MySqlConnector NuGet package

public class GameModCommands : ISlashCommand
{
    public string Name => "game";  // ✅ Changed from "gamemod" to "game"
    public string Description => "Game moderation commands";

    // ✅ Only these roles can use it
    private static readonly ulong[] AllowedRoleIds = new ulong[]
    {
        1393729574537396355UL, // Game Moderator
        1393590761953558608UL  // SM
    };

    // ✅ Direct database connection (as you asked, no RCON)
    private const string MYSQL_CONN =
        "Server=nw26472-001.eu.clouddb.ovh.net;" +
        "Port=35666;" +
        "Database=thefirm_qbcore;" +
        "User ID=thefirmprod;" +
        "Password=edr6BYZqmq7eud0mwm;" +
        "Character Set=utf8mb4;" +
        "SslMode=Required;";

    // ✅ Build: /game returnvehicle
    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("returnvehicle")
                .WithDescription("Return a vehicle to Legion Square by plate")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("plate", ApplicationCommandOptionType.String, "Vehicle plate, e.g. AB12 ABC", isRequired: true))
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
                    SET garage = @garage
                    WHERE UPPER(plate) = UPPER(@plate)
                    LIMIT 1;
                ";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@garage", "Legion Square");
                cmd.Parameters.AddWithValue("@plate", plate);

                affected = await cmd.ExecuteNonQueryAsync();
            }

            if (affected == 0)
                await command.FollowupAsync($"⚠️ No vehicle found with plate `{plate}`.", ephemeral: true);
            else
                await command.FollowupAsync($"✅ Vehicle `{plate}` has been moved to **Legion Square** garage.", ephemeral: true);
        }
        catch (Exception ex)
        {
            await command.FollowupAsync($"❌ Database error: `{ex.Message}`", ephemeral: true);
        }
    }
}
