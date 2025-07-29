using Discord.WebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class SlashCommandHandler
{
    private readonly Dictionary<string, ISlashCommand> _commands = new();

    private readonly IConfiguration _config;

    public SlashCommandHandler(IConfiguration config)
    {
        _config = config;
        
        _commands.Add("ticketclose", new TicketCloseCommand(_config));
        
        var joinCommand = new JoinCommand();
        _commands.Add(joinCommand.Name, joinCommand);

        var ForumLinkCommand = new ForumLinkCommand();
        _commands.Add(ForumLinkCommand.Name, ForumLinkCommand);

        var cleanupCommand = new CleanupCommand();
        _commands.Add(cleanupCommand.Name, cleanupCommand);

        var support = new SupportCommand();
        _commands.Add(support.Name, support);

        _commands.Add("ticketclaim", new TicketClaimCommand());
        _commands.Add("ticketrelease", new TicketReleaseCommand());
        _commands.Add("ticketadduser", new TicketAddUserCommand());
        _commands.Add("ticketaddrole", new TicketAddRoleCommand());
        _commands.Add("ticketresolve", new TicketResolveCommand());
        _commands.Add("ticketrestrict", new TicketRestrictCommand());
        _commands.Add("tempticket", new TempTicketCommand());

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
