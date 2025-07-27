using Discord.WebSocket;
using System.Threading.Tasks;

public class CloseTicketCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    public CloseTicketCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "close";
    public string Description => "Close the current ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await _ticketService.CloseTicketAsync(command);
    }
}
