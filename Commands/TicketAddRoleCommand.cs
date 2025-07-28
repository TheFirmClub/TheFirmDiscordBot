using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketAddRoleCommand : ISlashCommand
{
    public string Name => "ticketaddrole";
    public string Description => "Add a role to the ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        // Validate input
        if (command.Data.Options == null || !command.Data.Options.Any())
        {
            await command.RespondAsync("❌ No role was specified.", ephemeral: true);
            return;
        }

        var role = (SocketRole)command.Data.Options.First().Value;

        if (command.Channel is SocketTextChannel channel)
        {
            await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(
                viewChannel: PermValue.Allow,
                sendMessages: PermValue.Allow
            ));

            await command.RespondAsync($"✅ Added {role.Mention} to this ticket.");
        }
        else
        {
            await command.RespondAsync("❌ This command can only be used in a ticket channel.", ephemeral: true);
        }
    }
    
    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 ||
                r.Id == 1393623589122736238 ||
                r.Id == 1393638449709584434 ||
                r.Id == 1393590761953558608); 
        }

        public static bool IsSeniorModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393638449709584434 || 
                r.Id == 1393590761953558608);  
        }
    }
}