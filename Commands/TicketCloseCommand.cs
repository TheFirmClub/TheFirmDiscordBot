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
            await CloseTicketAsync(channel, user);
            await command.RespondAsync("✅ Ticket has been closed and archived.", ephemeral: true);
        }
    }

    public async Task CloseTicketAsync(SocketTextChannel channel, SocketGuildUser moderator)
    {
        var messages = await channel.GetMessagesAsync(int.MaxValue).FlattenAsync();
        var log = string.Join("\n", messages.OrderBy(m => m.Timestamp).Select(m =>
            $"[{m.Timestamp.UtcDateTime}] {m.Author.Username}: {m.Content}"));

        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, log);

        var logChannel = channel.Guild.GetTextChannel(1394449608603603085);
        if (logChannel != null)
        {
            // 📁 Upload transcript
            await logChannel.SendFileAsync(path, $"📁 Transcript for ticket `{channel.Name}` closed by {moderator.Mention}");

            // 📕 Log embed
            var logEmbed = new EmbedBuilder()
                .WithTitle("📕 Ticket Closed")
                .AddField("Closed By", moderator.Mention, true)
                .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", true)
                .WithColor(Color.DarkRed)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: logEmbed);
        }

        File.Delete(path);
        await channel.DeleteAsync();
    }

    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 || r.Id == 1393623589122736238 ||
                r.Id == 1393638449709584434); // include senior mods if needed
        }

        public static bool IsSeniorModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r => r.Id == 1393638449709584434);
        }
    }
}
