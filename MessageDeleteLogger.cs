using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.IO;

public class MessageDeleteLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _logChannelId;

    private static readonly TimeSpan AuditWindow = TimeSpan.FromSeconds(10);

    public MessageDeleteLogger(DiscordSocketClient client, ulong logChannelId)
    {
        _client = client;
        _logChannelId = logChannelId;

        _client.MessageDeleted += OnMessageDeleted;
        _client.MessagesBulkDeleted += OnMessagesBulkDeleted;
    }

    private async Task OnMessageDeleted(Cacheable<IMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        var channel = await cachedChannel.GetOrDownloadAsync() as SocketTextChannel;
        if (channel == null) return;

        var guild = channel.Guild;
        var logChannel = guild.GetTextChannel(_logChannelId);
        if (logChannel == null) return;

        var message = await cachedMessage.GetOrDownloadAsync();
        var content = message?.Content ?? "*Message content unavailable*";

        // Find who deleted it
        SocketGuildUser? moderator = await FindDeleterAsync(guild, channel.Id);

        var embed = new EmbedBuilder()
            .WithTitle("🗑️ Message Deleted")
            .WithColor(Color.Orange)
            .AddField("Channel", channel.Mention, true)
            .AddField("Author", message?.Author?.Mention ?? "Unknown", true)
            .AddField("Deleted By", moderator?.Mention ?? "Unknown", true)
            .AddField("Content", content.Length > 1024 ? content.Substring(0, 1021) + "..." : content)
            .WithFooter($"Message ID: {cachedMessage.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await logChannel.SendMessageAsync(embed: embed);
    }

    private async Task OnMessagesBulkDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> cachedMessages, Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        var channel = await cachedChannel.GetOrDownloadAsync() as SocketTextChannel;
        if (channel == null) return;

        var guild = channel.Guild;
        var logChannel = guild.GetTextChannel(_logChannelId);
        if (logChannel == null) return;

        // Try to find who purged
        SocketGuildUser? moderator = await FindDeleterAsync(guild, channel.Id);

        int count = cachedMessages.Count;
        string deletedBy = moderator?.Mention ?? "Unknown";

        var embed = new EmbedBuilder()
            .WithTitle("🗑️ Bulk Message Deletion")
            .WithColor(Color.Red)
            .AddField("Channel", channel.Mention, true)
            .AddField("Deleted By", deletedBy, true)
            .AddField("Messages Deleted", count.ToString(), true)
            .WithFooter($"Bulk purge event | Channel ID: {channel.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await logChannel.SendMessageAsync(embed: embed);

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
