using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketClaimCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    public TicketClaimCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "ticketclaim";
	public string Description => "Staff Member to Claim a Ticket";

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
            await command.RespondAsync("❌ You do not have permission to claim this ticket.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🎯 Ticket Claimed")
            .WithDescription($"{user.Mention} has claimed this ticket.")
            .WithColor(Color.Orange)
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
        await command.RespondAsync("✅ You have claimed the ticket.", ephemeral: true);
    }
}
