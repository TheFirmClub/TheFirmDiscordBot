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

        if (command.Channel is SocketTextChannel channel)
        {
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

            ulong seniorModRoleId = 123456789012345678; // Replace
            var seniorRole = channel.Guild.GetRole(seniorModRoleId);
            if (seniorRole != null)
            {
                await channel.AddPermissionOverwriteAsync(seniorRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
            }

            await command.RespondAsync("✅ Ticket resolved. Only Senior Moderators can view it.");
        }
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