using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes
    private readonly ulong _adminChannelId    = 1394451583709745272;  // Admin Logs
    private readonly ulong _mee6Id            = 1393611163853656085;  // MEE6-TheFirm Bot ID

    private static readonly string[] _moderationKeywords =
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
        // Only process embeds from MEE6 in the Admin Logs channel
        if (message.Author.Id != _mee6Id || message.Channel.Id != _adminChannelId)
            return;

        if (message is not IUserMessage userMessage || userMessage.Embeds.Count == 0)
            return;

        var embed = userMessage.Embeds.FirstOrDefault();
        if (embed == null)
            return;

        // Case-insensitive moderation keyword match in title or description
        bool containsModerationKeyword =
            _moderationKeywords.Any(k =>
                (!string.IsNullOrEmpty(embed.Title) && embed.Title.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(embed.Description) && embed.Description.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));

        if (!containsModerationKeyword)
            return;

        // Forward to Mod Notes
        if (_client.GetChannel(_modNotesChannelId) is not IMessageChannel modNotesChannel)
            return;

        var eb = new EmbedBuilder()
            .WithTitle(embed.Title ?? string.Empty)
            .WithDescription(embed.Description ?? string.Empty)
            .WithColor(embed.Color ?? Color.DarkBlue);

        if (embed.Timestamp.HasValue)
            eb.WithTimestamp(embed.Timestamp.Value);

        // Safe copies for footer/author across Discord.NET versions
        if (embed.Footer is { } footer)
            eb.WithFooter(footer.Text, footer.IconUrl);

        if (embed.Author is { } author)
            eb.WithAuthor(author.Name, author.IconUrl, author.Url);

        // Copy fields if any
        if (embed.Fields != null)
        {
            foreach (var f in embed.Fields)
            {
                if (f != null && !string.IsNullOrEmpty(f.Name))
                    eb.AddField(f.Name, f.Value ?? "\u200B", f.Inline);
            }
        }

        // (Optional) Copy thumbnail/image if present
        if (embed.Thumbnail is { } thumb && !string.IsNullOrEmpty(thumb.Url))
            eb.WithThumbnailUrl(thumb.Url);

        if (embed.Image is { } img && !string.IsNullOrEmpty(img.Url))
            eb.WithImageUrl(img.Url);

        await modNotesChannel.SendMessageAsync(embed: eb.Build());
    }
}
