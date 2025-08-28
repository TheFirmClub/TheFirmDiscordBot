using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _adminChannelId = 1393597248495030272;   // Admin Logs
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes
    private readonly ulong _mee6Id = 1393611163853656085;            // MEE6-TheFirm Bot ID

    // List of moderation keywords to filter
    private readonly string[] _moderationKeywords = new[]
    {
        "[MUTE]", "[UNMUTE]", "[BAN]", "[KICK]", "[WARN]", "[DEAFEN]", "[UNDEAFEN]"
    };

    public Mee6LogForwarder(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            if (message is not SocketUserMessage msg)
                return;

            // Only forward from Admin Logs channel
            if (msg.Channel.Id != _adminChannelId)
                return;

            // Only forward if it's from MEE6 (The Firm bot)
            if (msg.Author.Id != _mee6Id)
                return;

            // Check if this message contains a moderation event
            bool isModerationLog = false;

            // Check embeds first
            if (msg.Embeds.Count > 0)
            {
                foreach (var embed in msg.Embeds)
                {
                    // Check title, description, and fields for moderation keywords
                    if (!string.IsNullOrWhiteSpace(embed.Title))
                        isModerationLog |= _moderationKeywords.Any(k => embed.Title.Contains(k, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(embed.Description))
                        isModerationLog |= _moderationKeywords.Any(k => embed.Description.Contains(k, StringComparison.OrdinalIgnoreCase));

                    if (embed.Fields.Count > 0)
                        isModerationLog |= embed.Fields.Any(f => _moderationKeywords.Any(k =>
                            (!string.IsNullOrEmpty(f.Name) && f.Name.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(f.Value) && f.Value.Contains(k, StringComparison.OrdinalIgnoreCase))
                        ));
                }
            }

            // If not an embed, check content
            if (!isModerationLog && !string.IsNullOrWhiteSpace(msg.Content))
            {
                isModerationLog = _moderationKeywords.Any(k => msg.Content.Contains(k, StringComparison.OrdinalIgnoreCase));
            }

            if (!isModerationLog)
                return;

            // Get Mod Notes channel
            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null)
                return;

            // Forward embeds if present
            if (msg.Embeds.Count > 0)
            {
                foreach (var embed in msg.Embeds)
                {
                    var eb = new EmbedBuilder()
                        .WithAuthor(embed.Author?.Name, embed.Author?.IconUrl, embed.Author?.Url)
                        .WithColor(embed.Color ?? Color.Blue)
                        .WithDescription(embed.Description)
                        .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                        .WithImageUrl(embed.Image?.Url)
                        .WithThumbnailUrl(embed.Thumbnail?.Url)
                        .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow)
                        .WithTitle(embed.Title)
                        .WithUrl(embed.Url);

                    // Copy fields
                    foreach (var field in embed.Fields)
                    {
                        eb.AddField(field.Name, field.Value, field.Inline);
                    }

                    await modNotesChannel.SendMessageAsync(embed: eb.Build());
                }
            }
            else
            {
                // Fallback: plain text
                await modNotesChannel.SendMessageAsync(msg.Content);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error forwarding MEE6 log: {ex.Message}");
        }
    }
}
