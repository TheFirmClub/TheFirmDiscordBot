using Discord;
using Discord.WebSocket;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class TicketCloseCommand : ISlashCommand
{
    public string Name => "ticketclose";
    public string Description => "Close and archive the ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is SocketTextChannel channel)
        {
            var messages = await channel.GetMessagesAsync(int.MaxValue).FlattenAsync();
            var log = string.Join("\n", messages.OrderBy(m => m.Timestamp).Select(m =>
                $"[{m.Timestamp.UtcDateTime}] {m.Author.Username}: {m.Content}"));

            string path = Path.GetTempFileName();
            await File.WriteAllTextAsync(path, log);

            var logChannel = channel.Guild.GetTextChannel(1394449608603603085); // Replace with your log channel ID
            if (logChannel != null)
                await logChannel.SendFileAsync(path, $"Ticket Log: {channel.Name}");

            File.Delete(path);
            await channel.DeleteAsync();
        }
    }
    
    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 || r.Id == 1393623589122736238);
        }
    }
}