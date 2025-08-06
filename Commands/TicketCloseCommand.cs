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
        var user = command.User as SocketGuildUser;
        if (!PermissionHelper.IsSeniorModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is SocketTextChannel channel)
        {
            // ✅ Respond first before deletion
            await command.RespondAsync("✅ Ticket is being closed and archived...", ephemeral: true);
            await CloseTicketAsync(channel, user);
        }
    }

    public async Task CloseTicketAsync(SocketTextChannel channel, SocketGuildUser moderator)
    {
        Console.WriteLine(
            $"[DEBUG] CloseTicketAsync STARTED for channel: {channel.Name} ({channel.Id}) by moderator: {moderator.Username}");

        try
        {
            Console.WriteLine("[DEBUG] Fetching messages...");
            var messages = await channel.GetMessagesAsync(int.MaxValue).FlattenAsync();
            Console.WriteLine($"[DEBUG] Fetched {messages.Count()} messages");

            var log = string.Join("\n", messages.OrderBy(m => m.Timestamp).Select(m =>
                $"[{m.Timestamp.UtcDateTime}] {m.Author.Username}: {m.Content}"));

            string path = Path.GetTempFileName();
            Console.WriteLine($"[DEBUG] Writing transcript to temp file: {path}");
            await File.WriteAllTextAsync(path, log);

            string fileName = $"{channel.Name}-transcript.txt";

            // ✅ Upload to S3
            Console.WriteLine("[DEBUG] Uploading transcript to S3...");
            var s3 = new S3Bucket(_config);

            string s3Url = "";
            try
            {
                s3Url = await s3.UploadTranscriptAsync(path, fileName);
                Console.WriteLine($"[DEBUG] Uploaded to S3. URL: {s3Url}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to upload to S3: {ex.Message}");
            }

            var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
            if (logChannel != null)
            {
                Console.WriteLine(
                    $"[DEBUG] Sending transcript and log embed to log channel: {logChannel.Name} ({logChannel.Id})");

                await logChannel.SendMessageAsync(
                    $"📁 Transcript for ticket `{channel.Name}` uploaded by {moderator.Mention}:\n{s3Url}");

                var logEmbed = new EmbedBuilder()
                    .WithTitle("📕 Ticket Closed")
                    .AddField("Closed By", moderator.Mention, true)
                    .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", true)
                    .WithColor(Color.DarkRed)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: logEmbed);
            }
            else
            {
                Console.WriteLine("[WARN] Log channel not found.");
            }

            Console.WriteLine("[DEBUG] Deleting temp file...");
            File.Delete(path);

            Console.WriteLine($"[DEBUG] Deleting channel: {channel.Name} ({channel.Id})");
            await channel.DeleteAsync();
            Console.WriteLine("[DEBUG] Channel deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in CloseTicketAsync: {ex}");
        }

        Console.WriteLine("[DEBUG] CloseTicketAsync COMPLETED");
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
