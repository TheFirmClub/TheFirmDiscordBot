using Discord.WebSocket;
using System.Threading.Tasks;

public interface ISlashCommand
{
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(SocketSlashCommand command);
}
