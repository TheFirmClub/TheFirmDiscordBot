using Discord.WebSocket;
using System.Threading.Tasks;

public class TicketPanelCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    public TicketPanelCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "ticketpanel";
    public string Description => "Posts the ticket panel with categories";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await _ticketService.SendTicketPanelAsync(command.Channel);
        await command.RespondAsync("Ticket panel sent!", ephemeral: true);
    }
}
