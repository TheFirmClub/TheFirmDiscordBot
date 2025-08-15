using Discord;
using Discord.Net;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class AddRoleCommand : ISlashCommand
{
    public string Name => "addrole";
    public string Description => "Adds a role to a user";

    private static readonly HashSet<ulong> RestrictedRoleIds = new()
    {
        1393729574537396355, // Game Moderator
        1393623589122736238, // Discord Moderator
        1393638449709584434, // Senior Moderator
    };

    private static readonly HashSet<ulong> AllowedSpecialAssignerRoleIds = new()
    {
        1393590761953558608, // Senior Management
        1405330877440983130, // Assistant Head Moderator
        1393728468608487594, // Head Moderator
    };

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
        
        var targetUser = command.Data.Options.FirstOrDefault(o => o.Name == "user")?.Value as SocketGuildUser;
        var role = command.Data.Options.FirstOrDefault(o => o.Name == "role")?.Value as SocketRole;

        if (targetUser == null)
        {
            await command.RespondAsync("❌ Please specify a valid user.", ephemeral: true);
            return;
        }

        if (role == null)
        {
            await command.RespondAsync("❌ Please specify a valid role.", ephemeral: true);
            return;
        }

        if (RestrictedRoleIds.Contains(role.Id))
        {
            bool isSpecial = caller.Roles.Any(r => AllowedSpecialAssignerRoleIds.Contains(r.Id));
            if (!isSpecial)
            {
                await command.RespondAsync("❌ You are not allowed to assign that role.", ephemeral: true);
                return;
            }
        }

        if (targetUser.Roles.Any(r => r.Id == role.Id))
        {
            await command.RespondAsync($"ℹ️ {targetUser.Mention} already has `{role.Name}`.", ephemeral: true);
            return;
        }

        if (role.Position >= caller.Hierarchy)
        {
            await command.RespondAsync("❌ You cannot assign a role that is equal to or higher than your highest role.", ephemeral: true);
            return;
        }

        if (role.Position >= guild.CurrentUser.Hierarchy)
        {
            await command.RespondAsync("❌ I cannot assign that role because it's higher than my highest role.", ephemeral: true);
            return;
        }

        try
        {
            await targetUser.AddRoleAsync(role);
            await command.RespondAsync($"✅ Added role `{role.Name}` to {targetUser.Mention}.");
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            await command.RespondAsync("❌ I do not have permission to add that role.", ephemeral: true);
        }
    }
}
