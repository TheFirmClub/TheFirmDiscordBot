using Discord;
using Discord.WebSocket;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration; // add this at the top

public class TicketCloseCommand : ISlashCommand
{
    public string Name => "ticketclose";
    public string Description => "Close and archive the ticket";

    private readonly IConfiguration _config;

    public TicketCloseCommand(IConfiguration config)
    {
        _config = config;
    }
    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var guildChannel = command.Channel as SocketGuildChannel;
        if (guildChannel == null)
        {
            await command.RespondAsync("❌ This command must be used in a server channel.", ephemeral: true);
            return;
        }

        SocketGuild guild = guildChannel.Guild;
        SocketGuildUser user = guild.GetUser(command.User.Id); // ✅ use .GetUser here

        if (!PermissionHelper.IsSeniorModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is SocketTextChannel channel)
        {
            await command.RespondAsync("✅ Ticket is being closed and archived...", ephemeral: true);
            await CloseTicketAsync(channel, user);
        }
    }

    public async Task CloseTicketAsync(SocketTextChannel channel, SocketGuildUser moderator)
    {
        var messages = await channel.GetMessagesAsync(int.MaxValue).FlattenAsync();
        var log = string.Join("\n", messages.OrderBy(m => m.Timestamp).Select(m =>
            $"[{m.Timestamp.UtcDateTime}] {m.Author.Username}: {m.Content}"));

        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, log);

        string fileName = $"{channel.Name}-transcript.txt";

        // ✅ Upload to S3
        var s3 = new S3Bucket(_config);
        string s3Url = await s3.UploadTranscriptAsync(path, fileName);

        var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
        if (logChannel != null)
        {
            // 📎 Post S3 link
            await logChannel.SendMessageAsync($"📁 Transcript for ticket `{channel.Name}` uploaded by {moderator.Mention}:\n{s3Url}");

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
                r.Id == 1393729574537396355 ||
                r.Id == 1393623589122736238 ||
                r.Id == 1393638449709584434 ||
                r.Id == 1393590761953558608); 
        }

        public static bool IsSeniorModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393638449709584434 || 
                r.Id == 1393590761953558608);  
        }
    }
}
