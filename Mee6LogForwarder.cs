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

    private readonly string[] _moderationKeywords = new[]
    {
        "[MUTE]", "[UNMUTE]", "[BAN]", "[KICK]", "[WARN]", "[DEAFEN]", "[UNDEAFEN]"
    };

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (message is not SocketUserMessage msg)
            return;

        // Only process messages from Admin Logs channel
        if (msg.Channel.Id != _adminChannelId)
            return;

        // Only process messages from the MEE6 bot
        if (msg.Author.Id != _mee6Id)
            return;

        try
        {
            // Case-insensitive check for any moderation keyword
            bool isModerationLog = msg.Embeds.Any(embed =>
                _moderationKeywords.Any(keyword =>
                    (!string.IsNullOrEmpty(embed.Title) && embed.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(embed.Description) && embed.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                    embed.Fields.Any(f =>
                        (!string.IsNullOrEmpty(f.Name) && f.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(f.Value) && f.Value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    )
                )
            );

            if (!isModerationLog)
                return;

            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null)
                return;

            // Forward all matching embeds
            foreach (var embed in msg.Embeds)
            {
                var eb = new EmbedBuilder()
                    .WithAuthor(embed.Author?.Name, embed.Author?.IconUrl, embed.Author?.Url)
                    .WithColor(embed.Color ?? Color.Blue)
                    .WithDescription(embed.Description)
                    .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                    .WithImageUrl(embed.Image?.Url)
                    .WithThumbnail(embed.Thumbnail?.Url)
                    .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow)
                    .WithTitle(embed.Title)
                    .WithUrl(embed.Url);

                foreach (var field in embed.Fields)
                    eb.AddField(field.Name, field.Value, field.Inline);

                await modNotesChannel.SendMessageAsync(embed: eb.Build());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error forwarding MEE6 log: {ex.Message}");
        }
    }
}
