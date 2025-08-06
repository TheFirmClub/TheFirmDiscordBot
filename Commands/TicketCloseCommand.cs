using Discord;
using Discord.WebSocket;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

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
        Console.WriteLine("[TicketClose] Executing command...");

        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ This command must be used in a server.", ephemeral: true);
            Console.WriteLine("[TicketClose] Command was not used in a guild.");
            return;
        }

        if (!PermissionHelper.IsSeniorModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            Console.WriteLine("[TicketClose] Permission denied for user: " + user.Username);
            return;
        }

        if (command.Channel is SocketTextChannel channel)
        {
            Console.WriteLine("[TicketClose] Valid channel: " + channel.Name);
            await command.RespondAsync("✅ Ticket is being closed and archived...", ephemeral: true);
            await CloseTicketAsync(channel, user);
        }
        else
        {
            await command.RespondAsync("❌ This command must be used in a text channel.", ephemeral: true);
            Console.WriteLine("[TicketClose] Invalid channel.");
        }
    }

    public async Task CloseTicketAsync(SocketTextChannel channel, SocketGuildUser moderator)
    {
        Console.WriteLine("[TicketClose] Starting CloseTicketAsync for channel: " + channel.Name);

        try
        {
            var allMessages = new List<IMessage>();
            await foreach (var batch in channel.GetMessagesAsync())
            {
                allMessages.AddRange(batch);
            }

            Console.WriteLine("[TicketClose] Messages fetched: " + allMessages.Count);

            var log = string.Join("\n", allMessages.OrderBy(m => m.Timestamp).Select(m =>
                $"[{m.Timestamp.UtcDateTime}] {m.Author.Username}: {m.Content}"));

            string path = Path.GetTempFileName();
            await File.WriteAllTextAsync(path, log);
            Console.WriteLine("[TicketClose] Transcript written to file: " + path);

            string fileName = $"{channel.Name}-transcript.txt";

            var s3 = new S3Bucket(_config);
            string s3Url = await s3.UploadTranscriptAsync(path, fileName);
            Console.WriteLine("[TicketClose] Uploaded to S3: " + s3Url);

            var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
            if (logChannel != null)
            {
                await logChannel.SendMessageAsync($"📁 Transcript for ticket `{channel.Name}` uploaded by {moderator.Mention}:\n{s3Url}");

                var logEmbed = new EmbedBuilder()
                    .WithTitle("📕 Ticket Closed")
                    .AddField("Closed By", moderator.Mention, true)
                    .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", true)
                    .WithColor(Color.DarkRed)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: logEmbed);
                Console.WriteLine("[TicketClose] Log message sent.");
            }
            else
            {
                Console.WriteLine("[TicketClose] Log channel not found.");
            }

            File.Delete(path);
            Console.WriteLine("[TicketClose] Deleted temp file.");

            await channel.DeleteAsync();
            Console.WriteLine("[TicketClose] Deleted channel.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TicketClose] Exception: " + ex.Message);
        }
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
