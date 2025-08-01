using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using Discord.Net;

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
        var role = (SocketRole)command.Data.Options.First(o => o.Name == "role").Value;

        if (role == null)
        {
            await command.RespondAsync("❌ Role not found.");
            return;
        }

        // Role hierarchy checks
        if (role.Position >= caller.Hierarchy)
        {
            await command.RespondAsync("❌ You cannot remove a role that is equal or higher than your own highest role.", ephemeral: true);
            return;
        }

        if (role.Position >= guild.CurrentUser.Hierarchy)
        {
            await command.RespondAsync("❌ I cannot remove that role because it's higher than my highest role.", ephemeral: true);
            return;
        }

        try
        {
            await user.RemoveRoleAsync(role);
            await command.RespondAsync($"✅ Removed role `{role.Name}` from {user.Mention}.");
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            await command.RespondAsync("❌ I do not have permission to remove that role.", ephemeral: true);
        }
    }
}