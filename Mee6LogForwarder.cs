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

    // Moderation keywords
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

            if (msg.Channel.Id != _adminChannelId)
                return;

            if (msg.Author.Id != _mee6Id)
                return;

            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null)
                return;

            if (msg.Embeds.Count > 0)
            {
                foreach (var embed in msg.Embeds)
                {
                    bool isModerationLog = false;

                    // Check title, description, and fields
                    if (!string.IsNullOrEmpty(embed.Title))
                        isModerationLog |= _moderationKeywords.Any(k => embed.Title.Contains(k, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(embed.Description))
                        isModerationLog |= _moderationKeywords.Any(k => embed.Description.Contains(k, StringComparison.OrdinalIgnoreCase));

                    if (embed.Fields.Count > 0)
                        isModerationLog |= embed.Fields.Any(f => _moderationKeywords.Any(k => f.Name.Contains(k, StringComparison.OrdinalIgnoreCase)
                                                                                         || f.Value.Contains(k, StringComparison.OrdinalIgnoreCase)));

                    if (isModerationLog)
                    {
                        var eb = new EmbedBuilder()
                            .WithAuthor(embed.Author?.Name, embed.Author?.IconUrl, embed.Author?.Url)
                            .WithTitle(embed.Title)
                            .WithDescription(embed.Description)
                            .WithColor(embed.Color ?? Color.Blue)
                            .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                            .WithThumbnailUrl(embed.Thumbnail?.Url)
                            .WithImageUrl(embed.Image?.Url)
                            .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow);

                        foreach (var field in embed.Fields)
                            eb.AddField(field.Name, field.Value, field.Inline);

                        await modNotesChannel.SendMessageAsync(embed: eb.Build());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error forwarding MEE6 log: {ex.Message}");
        }
    }
}
