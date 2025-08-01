using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class RemoveRoleCommand : ISlashCommand
{
    public string Name => "removerole";
    public string Description => "Removes a role from a user";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller || !caller.GuildPermissions.ManageRoles)
        {
            await command.RespondAsync("❌ You need the **Manage Roles** permission to use this command.", ephemeral: true);
            return;
        }

        var guild = (command.Channel as SocketGuildChannel)?.Guild;
        if (guild == null)
        {
            await command.RespondAsync("❌ This command must be used in a server.");
            return;
        }

        var user = (SocketGuildUser)command.Data.Options.First(o => o.Name == "user").Value;
        var roleName = command.Data.Options.First(o => o.Name == "role").Value.ToString();
        var role = guild.Roles.FirstOrDefault(r => r.Name.Equals(roleName, System.StringComparison.OrdinalIgnoreCase));

        if (role == null)
        {
            await command.RespondAsync($"❌ Role `{roleName}` not found.");
            return;
        }

        await user.RemoveRoleAsync(role);
        await command.RespondAsync($"✅ Removed role `{role.Name}` from {user.Mention}.");
    }
}