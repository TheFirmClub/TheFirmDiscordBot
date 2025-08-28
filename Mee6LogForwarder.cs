using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;

    // Mod Notes channel ID
    private readonly ulong _modNotesChannelId = 1394451583709745273;

    // MEE6 bot ID
    private readonly ulong _mee6Id = 1393611163853656085;

    public Mee6LogForwarder(DiscordSocketClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        // Subscribe to message received event
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    public async Task OnMessageReceivedAsync(SocketMessage message)
    {
        // Only process messages from MEE6-TheFirm Bot
        if (message.Author.Id != _mee6Id) return;

        // Only process messages that have embeds
        if (message.Embeds.Count == 0) return;

        var embed = message.Embeds.First();

        // Extract all embed fields
        var embedFields = embed.Fields.ToList();

        // Only forward if both "User" and "Moderator" fields exist
        bool hasUserAndModerator = embedFields.Any(f => f.Name.Equals("User", StringComparison.OrdinalIgnoreCase))
                                 && embedFields.Any(f => f.Name.Equals("Moderator", StringComparison.OrdinalIgnoreCase));

        if (!hasUserAndModerator) return;

        // Determine the line for Mod Notes (preserve the [MUTE]/[UNMUTE]/etc)
        string forwardTitle = embed.Title;

        if (string.IsNullOrWhiteSpace(forwardTitle) && embed.Author != null)
        {
            forwardTitle = embed.Author.Name;
        }

        if (string.IsNullOrWhiteSpace(forwardTitle) && embedFields.Count > 0)
        {
            forwardTitle = embedFields[0].Value?.ToString() ?? string.Empty;
        }

        // Forward to Mod Notes channel
        var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
        if (modNotesChannel != null)
        {
            // Recreate the embed for Mod Notes
            var forwardEmbed = new EmbedBuilder()
                .WithTitle(forwardTitle)
                .WithDescription(embed.Description)
                .WithColor(embed.Color ?? Color.DarkRed)
                .WithTimestamp(embed.Timestamp ?? DateTimeOffset.Now)
                .WithFields(embedFields.Select(f => new EmbedFieldBuilder
                {
                    Name = f.Name,
                    Value = f.Value,
                    IsInline = f.Inline
                }))
                .Build();

            await modNotesChannel.SendMessageAsync(embed: forwardEmbed);
        }
    }
}
