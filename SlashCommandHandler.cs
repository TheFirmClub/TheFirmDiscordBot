using Discord.WebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;

public class SlashCommandHandler
{
    private readonly Dictionary<string, ISlashCommand> _commands = new();

    public SlashCommandHandler(DiscordSocketClient client)
    {
        // Register commands here
        var joinCommand = new JoinCommand();
        _commands.Add(joinCommand.Name, joinCommand);

        var cleanupCommand = new CleanupCommand();
        _commands.Add(cleanupCommand.Name, cleanupCommand);

        var closeTicketCommand = new CloseTicketCommand(client);  // ✅ Pass client here
        _commands.Add(closeTicketCommand.Name, closeTicketCommand);
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
