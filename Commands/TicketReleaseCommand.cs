using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketReleaseCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    // Add constructor to accept TicketService
    public TicketReleaseCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "ticketrelease";

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
            await command.RespondAsync("❌ You do not have permission to release this ticket.", ephemeral: true);
            return;
        }

        // Clear the claim by removing the topic
        await channel.ModifyAsync(prop => prop.Topic = null);

        var embed = new EmbedBuilder()
            .WithTitle("🔓 Ticket Released")
            .WithDescription($"{user.Mention} has released this ticket for others to claim.")
            .WithColor(Color.LightGrey)
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
        await command.RespondAsync("✅ You have released the ticket.", ephemeral: true);
    }
}
