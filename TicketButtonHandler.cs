using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketButtonHandler
{
    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355,
        1393623589122736238
    };

    public async Task HandleAsync(SocketMessageComponent component)
    {
        var user = component.User as SocketGuildUser;
        var isMod = user.Roles.Any(r => _moderatorRoleIds.Contains(r.Id));

        if (!isMod)
        {
            await component.RespondAsync("❌ Only moderators can use this.", ephemeral: true);
            return;
        }

        switch (component.Data.CustomId)
        {
            case "ticket_claim":
                await component.RespondAsync($"🎯 Ticket claimed by {user.Mention}", ephemeral: false);
                break;

            case "ticket_release":
                await component.RespondAsync($"🔓 Ticket released by {user.Mention}", ephemeral: false);
                break;

            default:
                await component.RespondAsync("❓ Unknown action.", ephemeral: true);
                break;
        }
    }
}