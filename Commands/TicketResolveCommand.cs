using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketResolveCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    public TicketResolveCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "ticketresolve";
	public string Description => "Resolve a Ticket";

    private readonly ulong[] _staffRoleIds = { 1393623589122736238 }; // Discord Moderator role ID

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        var channel = command.Channel as SocketTextChannel;

        if (channel == null || !channel.Name.StartsWith("ticket-"))
        {
            await command.RespondAsync("❌ This command can only be used in a ticket channel.", ephemeral: true);
            return;
        }

        if (!_staffRoleIds.Any(roleId => user.Roles.Any(r => r.Id == roleId)))
        {
            await command.RespondAsync("❌ You do not have permission to resolve this ticket.", ephemeral: true);
            return;
        }

        var guild = channel.Guild;

        var ticketOwnerId = channel.PermissionOverwrites
            .Where(o => o.TargetType == PermissionTarget.User)
            .Select(o => o.TargetId)
            .FirstOrDefault(id => id != user.Id && id != guild.EveryoneRole.Id);

        var ticketOwner = guild.GetUser(ticketOwnerId);

        if (ticketOwner == null)
        {
            await command.RespondAsync("❌ Could not find the ticket owner.", ephemeral: true);
            return;
        }

        var dm = await ticketOwner.GetOrCreateDMChannelAsync();
        var builder = new ComponentBuilder()
            .WithButton("Accept Resolution", $"ticket_resolve_accept_{channel.Id}", ButtonStyle.Success)
            .WithButton("Reject Resolution", $"ticket_resolve_reject_{channel.Id}", ButtonStyle.Danger);

        await dm.SendMessageAsync($"Your ticket in #{channel.Name} was resolved. Accept?", components: builder.Build());
        await command.RespondAsync("Resolution prompt sent.", ephemeral: true);
    }
}
