using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class MessageDeleteLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _logChannelId;

    public MessageDeleteLogger(DiscordSocketClient client, ulong logChannelId)
    {
        _client = client;
        _logChannelId = logChannelId;

        _client.MessageDeleted += OnMessageDeletedAsync;
    }

    private async Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        try
        {
            var channel = await cachedChannel.GetOrDownloadAsync();
            var guildChannel = channel as SocketGuildChannel;
            if (guildChannel == null) return;

            var guild = guildChannel.Guild;
            var logChannel = guild.GetTextChannel(_logChannelId);
            if (logChannel == null) return;

            // Get the deleted message (if cached)
            var message = cachedMessage.HasValue ? cachedMessage.Value : null;
            string messageContent = message?.Content ?? "*Unknown or uncached content*";

            // Try to find who deleted the message from audit logs
            string deletedBy = "Unknown";
            var logs = await guild.GetAuditLogsAsync(1, actionType: ActionType.MessageDeleted).FlattenAsync();
            var entry = logs.FirstOrDefault();
            if (entry != null && entry is MessageDeleteAuditLogEntry msgEntry)
            {
                deletedBy = msgEntry.User?.ToString() ?? "Unknown";
            }

            var embed = new EmbedBuilder()
                .WithTitle("🗑️ Message Deleted")
                .WithColor(Color.Red)
                .AddField("Channel", channel.Name, true)
                .AddField("Deleted By", deletedBy, true)
                .AddField("Message Content", messageContent.Length > 0 ? messageContent : "*No text (embed/attachment)*")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error logging deleted message: {ex.Message}");
        }
    }
}

// Save deleted messages into a text file
var logLines = new List<string>();
        foreach (var cachedMsg in cachedMessages)
        {
            var msg = await cachedMsg.GetOrDownloadAsync();
            string author = msg?.Author?.ToString() ?? "Unknown";
            string content = msg?.Content ?? "[No Content]";
            logLines.Add($"[{msg?.Timestamp.UtcDateTime}] {author}: {content}");
        }

        if (logLines.Count > 0)
        {
            string logText = string.Join(Environment.NewLine, logLines);
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(logText)))
            {
                await logChannel.SendFileAsync(stream, "purge-log.txt", "📝 Bulk purge log");
            }
        }
    }

    private async Task<SocketGuildUser?> FindDeleterAsync(SocketGuild guild, ulong channelId)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var logs = await guild.GetAuditLogsAsync(10, actionType: ActionType.MessageDeleted).FlattenAsync();

            foreach (var entry in logs)
            {
                if (entry.CreatedAt < now - AuditWindow)
                    continue;

                var data = entry.Data as MessageDeleteAuditLogData;
                if (data != null && data.ChannelId == channelId)
                {
                    return guild.GetUser(entry.User.Id);
                }
            }
        }
        catch
        {
            // Ignore audit log errors
        }

        return null;
    }
}
