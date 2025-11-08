using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CoreRCON;
using Discord;
using Discord.WebSocket;

public class GameModCommands : ISlashCommand
{
    public string Name => "gamemod";
    public string Description => "Game moderation commands (e.g., return a vehicle to a garage)";

    // --- RCON CONFIG (hardcoded as requested) ---
    private const string RCON_HOST = "127.0.0.1";         // ← change if your bot is remote
    private const ushort RCON_PORT = 30120;               // ← your FiveM server port
    private const string RCON_PASSWORD = "rc0nsmallp13test";      // ← set a strong password

    // --- Allowed Discord roles ---
    private static readonly ulong[] AllowedRoleIds = new ulong[]
    {
        1393729574537396355UL, // Game Moderator
        1393590761953558608UL, // SM
    };

    // Build the /gamemod command with a single subcommand: /gamemod returnvehicle
    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("returnvehicle")
                .WithDescription("Set a vehicle's garage by plate (server-side)")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("plate", ApplicationCommandOptionType.String, "Vehicle plate, e.g. AB12 ABC", isRequired: true))
            .Build();
    }

    // Required by your ISlashCommand interface
    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        // Guild-only + role gate
        if (command.GuildId == null)
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        var member = command.User as IGuildUser;
        if (member == null || !member.RoleIds.Any(rid => AllowedRoleIds.Contains(rid)))
        {
            await command.RespondAsync("You don't have permission to use this command.", ephemeral: true);
            return;
        }

        // We only have one subcommand: returnvehicle
        var sub = command.Data.Options.First().Name.ToLowerInvariant();

        await command.DeferAsync(ephemeral: true);

        switch (sub)
        {
            case "returnvehicle":
                await HandleReturnVehicle(command, command.Data.Options.First().Options);
                break;

            default:
                await command.FollowupAsync("Unknown subcommand.", ephemeral: true);
                break;
        }
    }

    // /gamemod returnvehicle plate:<text>
    private async Task HandleReturnVehicle(SocketSlashCommand command, System.Collections.Generic.IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        var plate = options.First(o => o.Name == "plate").Value?.ToString()?.Trim() ?? string.Empty;

        // Basic input hardening: letters/numbers/spaces, up to 12 chars (tweak to your server’s plate rules)
        if (!Regex.IsMatch(plate, @"^[A-Za-z0-9 ]{1,12}$"))
        {
            await command.FollowupAsync("❌ Invalid plate format. Use letters/numbers/spaces only (max 12).", ephemeral: true);
            return;
        }

        try
        {
            using var rcon = new RCON(IPAddress.Parse(RCON_HOST), RCON_PORT, RCON_PASSWORD, timeout: 5000);
            await rcon.ConnectAsync();

            // Call the FiveM console command you created in your Lua resource
            var reply = await rcon.SendCommandAsync($"returnvehicle {plate}");

            if (string.IsNullOrWhiteSpace(reply))
                reply = "Command sent. Check server console/logs for details.";

            await command.FollowupAsync($"✅ Sent `returnvehicle {plate}` to the server.\n```\n{reply}\n```", ephemeral: true);
        }
        catch (Exception ex)
        {
            await command.FollowupAsync($"❌ RCON error: `{ex.Message}`", ephemeral: true);
        }
    }
}
