using Discord.WebSocket;
using System.Threading.Tasks;

public class TicketPanelCommand : ISlashCommand
{
    public string Name => "ticketpanel";
    public string Description => "Send the ticket panel to this channel.";

    private readonly TicketService _ticketService;

    public TicketPanelCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var channel = command.Channel;

        await _ticketService.SendTicketPanelAsync(channel);

        await command.RespondAsync("✅ Ticket panel sent!", ephemeral: true);
    }
}
