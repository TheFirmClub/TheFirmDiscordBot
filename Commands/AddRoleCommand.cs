using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using Discord.Net;

public class AddRoleCommand : ISlashCommand
{
    public string Name => "addrole";
    public string Description => "Adds a role to a user";

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
            await command.RespondAsync("❌ This command must be used in a server.", ephemeral: true);
            return;
        }

        var user = (SocketGuildUser)command.Data.Options.First(o => o.Name == "user").Value;
        var role = (SocketRole)command.Data.Options.First(o => o.Name == "role").Value;

        if (role == null)
        {
            await command.RespondAsync("❌ Role not found.", ephemeral: true);
            return;
        }

        // Role hierarchy check: caller must be higher than the target role
        if (role.Position >= caller.Hierarchy)
        {
            await command.RespondAsync("❌ You cannot assign a role that is equal to or higher than your own highest role.", ephemeral: true);
            return;
        }

        // Role hierarchy check: bot must be higher than the target role
        if (role.Position >= guild.CurrentUser.Hierarchy)
        {
            await command.RespondAsync("❌ I cannot assign that role because it's higher than my highest role.", ephemeral: true);
            return;
        }

        try
        {
            await user.AddRoleAsync(role);
            await command.RespondAsync($"✅ Added role `{role.Name}` to {user.Mention}.");
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            await command.RespondAsync("❌ I do not have permission to add that role.", ephemeral: true);
        }
    }
}
