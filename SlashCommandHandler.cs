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

        // General Commands
        var joinCommand = new JoinCommand();
        _commands.Add(joinCommand.Name, joinCommand);

        var cleanupCommand = new CleanupCommand();
        _commands.Add(cleanupCommand.Name, cleanupCommand);

        // Ticket Panel
        var ticketPanelCommand = new TicketPanelCommand(_ticketService);
        _commands.Add(ticketPanelCommand.Name, ticketPanelCommand);

        // Ticket System Commands
        var closeTicketCommand = new CloseTicketCommand(_ticketService);
        _commands.Add(closeTicketCommand.Name, closeTicketCommand);

        var ticketAddRole = new TicketAddRoleCommand(_ticketService);
        _commands.Add(ticketAddRole.Name, ticketAddRole);

        var ticketAddUser = new TicketAddUserCommand(_ticketService);
        _commands.Add(ticketAddUser.Name, ticketAddUser);

        var ticketClaim = new TicketClaimCommand(_ticketService);
        _commands.Add(ticketClaim.Name, ticketClaim);

        var ticketRelease = new TicketReleaseCommand(_ticketService);
        _commands.Add(ticketRelease.Name, ticketRelease);

        var ticketResolve = new TicketResolveCommand(_ticketService);
        _commands.Add(ticketResolve.Name, ticketResolve);

        var ticketRestrict = new TicketRestrictCommand(_ticketService);
        _commands.Add(ticketRestrict.Name, ticketRestrict);

        var tempTicket = new TempTicketCommand(_ticketService);
        _commands.Add(tempTicket.Name, tempTicket);
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
