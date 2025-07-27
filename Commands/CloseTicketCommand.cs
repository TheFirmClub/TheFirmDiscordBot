using Discord.WebSocket;
using System.Threading.Tasks;

public class CloseTicketCommand : ISlashCommand
{
    public string Name => "close";
    public string Description => "Closes the current ticket.";

    private readonly TicketService _ticketService;

    // ✅ Constructor that accepts the client
    public CloseTicketCommand(DiscordSocketClient client)
    {
        _ticketService = new TicketService(client);
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await _ticketService.CloseTicketAsync(command);
    }
}
