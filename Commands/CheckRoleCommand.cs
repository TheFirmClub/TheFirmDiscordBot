using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class CheckRoleCommand : ISlashCommand
{
    public string Name => "checkrole";
    public string Description => "Lists all roles a user has";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller || !caller.GuildPermissions.ManageRoles)
        {
            await command.RespondAsync("❌ You need the **Manage Roles** permission to use this command.", ephemeral: true);
            return;
        }

        var user = (SocketGuildUser)command.Data.Options.First(o => o.Name == "user").Value;

        var roles = user.Roles.Where(r => !r.IsEveryone).Select(r => r.Name);
        var roleList = roles.Any() ? string.Join(", ", roles) : "*No roles*";

        await command.RespondAsync($"👤 {user.Username}'s roles:\n```\n{roleList}\n```");
    }
}