using Discord.WebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;

public class SlashCommandHandler
{
    private readonly Dictionary<string, ISlashCommand> _commands = new();
    private readonly TicketService _ticketService;

    public SlashCommandHandler(TicketService ticketService)
    {
        _ticketService = ticketService;

        // Register commands here
        var joinCommand = new JoinCommand();
        _commands.Add(joinCommand.Name, joinCommand);

        var cleanupCommand = new CleanupCommand();
        _commands.Add(cleanupCommand.Name, cleanupCommand);

        // Register the close ticket command passing TicketService
        var closeTicketCommand = new CloseTicketCommand(_ticketService);
        _commands.Add(closeTicketCommand.Name, closeTicketCommand);

        var ticketPanelCommand = new TicketPanelCommand(_ticketService);
        _commands.Add(ticketPanelCommand.Name, ticketPanelCommand);

    }

    public async Task HandleCommandAsync(SocketSlashCommand command)
    {
        if (_commands.TryGetValue(command.CommandName, out var handler))
        {
            await handler.ExecuteAsync(command);
        }
        else
        {
            await command.RespondAsync("Command not recognized.");
        }
    }

    public IEnumerable<ISlashCommand> GetAllCommands() => _commands.Values;
}
