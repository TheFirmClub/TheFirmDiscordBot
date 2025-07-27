using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class TicketResolveCommand : ISlashCommand
{
    public string Name => "ticketresolve";
    public string Description => "Mark the ticket as resolved";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.RespondAsync("❌ This command can only be used in a ticket channel.", ephemeral: true);
            return;
        }

        // Remove all current overwrites
        foreach (var overwrite in channel.PermissionOverwrites)
        {
            if (overwrite.TargetType == PermissionTarget.User)
            {
                var u = channel.Guild.GetUser(overwrite.TargetId);
                if (u != null)
                    await channel.RemovePermissionOverwriteAsync(u);
            }
            else if (overwrite.TargetType == PermissionTarget.Role)
            {
                var r = channel.Guild.GetRole(overwrite.TargetId);
                if (r != null)
                    await channel.RemovePermissionOverwriteAsync(r);
            }
        }

        
        await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
            new OverwritePermissions(viewChannel: PermValue.Deny));
        
        ulong seniorModRoleId = 1393638449709584434;
        var seniorRole = channel.Guild.GetRole(seniorModRoleId);
        if (seniorRole != null)
        {
            await channel.AddPermissionOverwriteAsync(seniorRole,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        }

        await command.RespondAsync("✅ Ticket resolved. Only Senior Moderators can now view this ticket.");
    }
    
    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 || r.Id == 1393623589122736238);
        }
    }
}