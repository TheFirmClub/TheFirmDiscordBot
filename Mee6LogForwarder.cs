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
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        // Only handle messages from the MEE6 bot
        if (message.Author.Id != _mee6Id)
            return;

        // Only handle messages in the Admin Logs channel
        if (message.Channel.Id != _adminChannelId)
            return;

        // Only handle embeds
        if (message is not IUserMessage userMessage || userMessage.Embeds.Count == 0)
            return;

        foreach (var embed in userMessage.Embeds)
        {
            // Skip non-moderation embeds
            if (!_moderationKeywords.Any(k => embed.Title != null && embed.Title.Contains(k)))
                continue;

            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null)
                continue;

            var eb = new EmbedBuilder()
                .WithTitle(embed.Title)
                .WithDescription(embed.Description)
                .WithColor(embed.Color ?? Color.LightGrey)
                .WithFooter(embed.Footer?.Text)
                .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow);

            // Copy fields safely
            foreach (var field in embed.Fields)
            {
                eb.AddField(field.Name, field.Value, field.Inline);
            }

            // Send to Mod Notes
            await modNotesChannel.SendMessageAsync(embed: eb.Build());
        }
    }
}
