using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6Forwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _adminChannelId = 1393597248495030272;   // Admin Logs
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes
    private readonly ulong _mee6Id = 1393611163853656085; // The Firm (MEE6 custom bot) ID

    // List of moderation keywords to filter embeds
    private readonly string[] _moderationKeywords = new[] { "mute", "banned", "ban", "kicked", "deafen", "warn" };

    public Mee6Forwarder(DiscordSocketClient client)
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

            // Get Mod Notes channel
            if (_client.GetChannel(_modNotesChannelId) is not IMessageChannel modNotesChannel)
                return;

            bool shouldForward = false;

            // Check if embed contains moderation keyword
            if (msg.Embeds.Count > 0)
            {
                foreach (var embed in msg.Embeds)
                {
                    string embedText = string.Join(" ", new[]
                    {
                        embed.Title,
                        embed.Description,
                        embed.Footer?.Text ?? "",
                        string.Join(" ", embed.Fields.Select(f => f.Name + " " + f.Value))
                    }).ToLower();

                    if (_moderationKeywords.Any(k => embedText.Contains(k)))
                    {
                        shouldForward = true;

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

                        foreach (var field in embed.Fields)
                            eb.AddField(field.Name, field.Value, field.Inline);

                        await modNotesChannel.SendMessageAsync(embed: eb.Build());
                    }
                }
            }

            // Optional: plain text fallback for moderation keywords
            if (!shouldForward && !string.IsNullOrWhiteSpace(msg.Content))
            {
                string text = msg.Content.ToLower();
                if (_moderationKeywords.Any(k => text.Contains(k)))
                {
                    await modNotesChannel.SendMessageAsync(
                        $"📢 **Forwarded from Admin Logs:**\n{msg.Content}"
                    );
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error forwarding MEE6 log: {ex.Message}");
        }
    }
}
