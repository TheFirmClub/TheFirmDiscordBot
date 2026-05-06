using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ListRolesCommand : ISlashCommand
{
    public string Name => "listroles";
    public string Description => "Lists all server roles with their IDs";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        // Permission check
        if (command.User is not SocketGuildUser caller || !caller.GuildPermissions.ManageRoles)
        {
            await command.RespondAsync(
                "❌ You need the **Manage Roles** permission to use this command.",
                ephemeral: true
            );
            return;
        }

        var guild = caller.Guild;

        // Sort roles by position (highest first)
        var roles = guild.Roles
            .OrderByDescending(r => r.Position)
            .Where(r => !r.IsEveryone);

        var sb = new StringBuilder();

        foreach (var role in roles)
        {
            sb.AppendLine($"{role.Name} -> {role.Id}");
        }

        // Discord message limit protection
        var output = sb.Length > 1900
            ? sb.ToString().Substring(0, 1900) + "\n..."
            : sb.ToString();

        await command.RespondAsync(
            $"📜 Server Roles for **{guild.Name}**:\n```{output}```",
            ephemeral: true
        );
    }
}