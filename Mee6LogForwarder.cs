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
    private readonly ulong _mee6Id = 1393611163853656085;            // MEE6-TheFirm Bot ID

    // List of moderation keywords
    private readonly string[] _moderationKeywords = new[]
    {
        "MUTE", "UNMUTE", "BAN", "KICK", "WARN", "DEAFEN", "UNDEAFEN"
    };

    public Mee6LogForwarder(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    public async Task OnMessageReceivedAsync(SocketMessage message)
    {
        // Only process messages from MEE6-TheFirm Bot
        if (message.Author.Id != _mee6Id) return;

        // Only process embedded messages
        if (message.Embeds.Count == 0) return;

        var embed = message.Embeds.First();

        // Extract all embed fields
        var embedFields = embed.Fields.ToList();

        // Only forward if both "User" and "Moderator" fields exist
        bool hasUserAndModerator = embedFields.Any(f => f.Name.Equals("User", StringComparison.OrdinalIgnoreCase))
                                 && embedFields.Any(f => f.Name.Equals("Moderator", StringComparison.OrdinalIgnoreCase));

        if (!hasUserAndModerator) return;

        // Check if the embed description contains any moderation keyword
        string embedText = embed.Description ?? string.Empty;
        bool isModerationAction = _moderationKeywords.Any(k => embedText.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (!isModerationAction) return;

        // Forward to Mod Notes channel
        var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
        if (modNotesChannel != null)
        {
            var forwardEmbed = new EmbedBuilder()
                .WithTitle(embed.Title)
                .WithDescription(embed.Description)
                .WithColor(embed.Color ?? Color.DarkRed)
                .WithTimestamp(embed.Timestamp ?? DateTimeOffset.Now)
                .WithFields(embedFields)
                .Build();

            await modNotesChannel.SendMessageAsync(embed: forwardEmbed);
        }
    }
}
