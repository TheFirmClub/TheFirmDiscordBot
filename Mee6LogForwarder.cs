using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes
    private readonly ulong _adminChannelId = 1394451583709745272;    // Admin Logs
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
        // Only process messages from MEE6 in the Admin Logs channel
        if (message.Author.Id != _mee6Id || message.Channel.Id != _adminChannelId)
            return;

        if (message is not IUserMessage userMessage || userMessage.Embeds.Count == 0)
            return;

        var embed = userMessage.Embeds.FirstOrDefault();
        if (embed == null)
            return;

        // Check Title OR Description for moderation keywords
        bool containsModerationKeyword =
            _moderationKeywords.Any(k => (embed.Title != null && embed.Title.Contains(k))) ||
            _moderationKeywords.Any(k => (embed.Description != null && embed.Description.Contains(k)));

        if (!containsModerationKeyword)
            return;

        // Forward the embed to Mod Notes
        var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
        if (modNotesChannel == null)
            return;

        var embedBuilder = new EmbedBuilder()
            .WithTitle(embed.Title ?? "")
            .WithDescription(embed.Description ?? "")
            .WithColor(embed.Color ?? Color.DarkBlue);

        // Copy fields safely
        foreach (var f in embed.Fields)
        {
            embedBuilder.AddField(f.Name, f.Value, f.Inline); // ✅ correct property
        }

        // (optional) preserve footer, timestamp, author if present
        if (embed.Timestamp.HasValue)
            embedBuilder.WithTimestamp(embed.Timestamp.Value);

        if (embed.Footer.HasValue)
            embedBuilder.WithFooter(embed.Footer.Value.Text, embed.Footer.Value.IconUrl);

        if (embed.Author.HasValue)
            embedBuilder.WithAuthor(embed.Author.Value.Name, embed.Author.Value.IconUrl, embed.Author.Value.Url);

        await modNotesChannel.SendMessageAsync(embed: embedBuilder.Build());
    }
}
