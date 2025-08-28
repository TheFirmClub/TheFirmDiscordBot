using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;

    // Hardcoded channel IDs here
    private readonly ulong _adminChannelId = 1393597248495030272;   // Admin Logs
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes

    public Mee6LogForwarder(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            // Only care about messages in Admin Logs channel
            if (message.Channel.Id != _adminChannelId) return;

            // Ignore if it's not from a bot (we only want MEE6 / system logs)
            if (!message.Author.IsBot) return;

            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null) return;

            // Case 1: Message has embeds (like your [MUTE] log)
            if (message.Embeds.Any())
            {
                foreach (var embed in message.Embeds)
                {
                    var builder = new EmbedBuilder()
                        .WithAuthor(embed.Author?.Name ?? "MEE6 Log", embed.Author?.IconUrl, embed.Author?.Url)
                        .WithTitle(embed.Title)
                        .WithDescription(embed.Description)
                        .WithColor(embed.Color ?? Color.DarkGrey)
                        .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                        .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow);

                    // Copy all fields
                    foreach (var field in embed.Fields)
                    {
                        builder.AddField(field.Name, field.Value, field.Inline);
                    }

                    await modNotesChannel.SendMessageAsync(embed: builder.Build());
                }
            }

            // Case 2: Message has plain text content (sometimes MEE6 does this)
            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                await modNotesChannel.SendMessageAsync($"📋 **MEE6 Log Message:** {message.Content}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Mee6LogForwarder] Error forwarding message: {ex}");
        }
    }
}
