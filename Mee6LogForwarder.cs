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

    // List of moderation keywords to detect
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
            if (message.Author.Id != _mee6Id) return;          // Only MEE6 messages
            if (message.Channel.Id != _adminChannelId) return; // Only from Admin Logs
            if (message.Embeds.Count == 0) return;            // Only embeds

            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null) return;

            foreach (var embed in message.Embeds)
            {
                string description = embed.Description ?? string.Empty;

                // Check if description contains any moderation keyword
                if (!_moderationKeywords.Any(k => description.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Build safe embed copy
                var eb = new EmbedBuilder()
                    .WithTitle(embed.Title)
                    .WithDescription(description)
                    .WithColor(embed.Color ?? Color.Blue)
                    .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                    .WithImageUrl(embed.Image?.Url)
                    .WithThumbnailUrl(embed.Thumbnail?.Url)
                    .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow);

                // Copy fields if any
                foreach (var field in embed.Fields ?? Array.Empty<EmbedFieldBuilder>())
                {
                    eb.AddField(field.Name, field.Value, field.Inline);
                }

                await modNotesChannel.SendMessageAsync(embed: eb.Build());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error forwarding MEE6 log: {ex.Message}");
        }
    }
}
