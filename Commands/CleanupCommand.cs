using Discord;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CleanupCommand : ISlashCommand
{
    public string Name => "cleanup";
    public string Description => "Deletes outdated or unused slash commands.";

    private readonly ulong AllowedUserId = 1193354082849673297; // Replace with your ID

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User.Id != AllowedUserId)
        {
            await command.RespondAsync("❌ You are not authorised to run this command.", ephemeral: true);
            return;
        }

        var guild = (command.Channel as SocketGuildChannel)?.Guild;
        if (guild == null)
        {
            await command.RespondAsync("❌ This command must be used in a server (guild).", ephemeral: true);
            return;
        }

        var deletedNames = new List<string>();
        var obsoleteNames = new List<string> { "embed", "oldcommand", "close", "tempticket", "ticketaddrole", "ticketadduser", "ticketclaim", "ticketpanel", "ticketrelease", "ticketresolve", "ticketrestrict" }; // Add names here

        var existingCommands = await guild.GetApplicationCommandsAsync();
        foreach (var cmd in existingCommands)
        {
            if (obsoleteNames.Contains(cmd.Name))
            {
                await cmd.DeleteAsync();
                deletedNames.Add(cmd.Name);
            }
        }

        string response = deletedNames.Count > 0
            ? $"🗑️ Deleted obsolete commands: `{string.Join("`, `", deletedNames)}`"
            : "✅ No obsolete commands found.";

        await command.RespondAsync(response, ephemeral: true);
    }
}
