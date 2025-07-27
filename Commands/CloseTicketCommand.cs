using Discord.WebSocket;
using System.Threading.Tasks;

public class CloseTicketCommand : ISlashCommand
{
    public string Name => "close";
    public string Description => "Closes the current ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var ticketService = new TicketService();
        await ticketService.CloseTicketAsync(command);
    }
}
