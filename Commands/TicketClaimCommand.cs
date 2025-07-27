using Discord.WebSocket;
using System.Threading.Tasks;

public class TicketClaimCommand : ISlashCommand
{
    public string Name => "ticketclaim";
    public string Description => "Claim a ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        await command.RespondAsync("🔒 Ticket claimed by " + command.User.Mention);
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