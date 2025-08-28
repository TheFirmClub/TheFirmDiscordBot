using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;

    // Channel IDs
    private readonly ulong _adminChannelId = 1393597248495030272;   // Admin Logs
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes

    // Only forward these types of logs
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
            // Only process messages in Admin Logs channel
            if (message.Channel.Id != _adminChannelId) return;

            // Ignore if not from a bot (so random chatter doesn’t get logged)
            if (!message.Author.IsBot) return;

            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null) return;

            bool containsModerationKeyword = false;

            // Check embeds for moderation keywords
            if (message.Embeds.Any())
            {
                foreach (var embed in message.Embeds)
                {
                    string embedText = $"{embed.Title} {embed.Description} " +
                                       string.Join(" ", embed.Fields.Select(f => f.Name + " " + f.Value)) +
                                       $" {embed.Footer?.Text}";

                    if (_moderationKeywords.Any(k => embedText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        containsModerationKeyword = true;

                        var builder = new EmbedBuilder()
                            .WithAuthor(embed.Author?.Name ?? "MEE6 Log", embed.Author?.IconUrl, embed.Author?.Url)
                            .WithTitle(embed.Title)
                            .WithDescription(embed.Description)
                            .WithColor(embed.Color ?? Color.DarkGrey)
                            .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                            .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow);

                        foreach (var field in embed.Fields)
                        {
                            builder.AddField(field.Name, field.Value, field.Inline);
                        }

                        await modNotesChannel.SendMessageAsync(embed: builder.Build());
                    }
                }
            }

            // Check plain text content
            if (!containsModerationKeyword && !string.IsNullOrWhiteSpace(message.Content))
            {
                if (_moderationKeywords.Any(k => message.Content.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    await modNotesChannel.SendMessageAsync($"📋 **MEE6 Log Message:** {message.Content}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Mee6LogForwarder] Error forwarding message: {ex}");
        }
    }
}
