using Discord;
using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class Mee6Forwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _adminChannelId = 1393597248495030272;   // Admin Logs
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes
    private readonly ulong _mee6Id = 1393611163853656085; // The Firm (Mee6 custom bot) ID

    public Mee6Forwarder(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
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
        var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
        if (modNotesChannel == null)
            return;

        // Forward embeds if they exist
        if (msg.Embeds.Count > 0)
        {
            foreach (var embed in msg.Embeds)
            {
                var eb = new EmbedBuilder()
                    .WithAuthor(embed.Author?.Name, embed.Author?.IconUrl, embed.Author?.Url)
                    .WithColor(embed.Color ?? Color.Blue)
                    .WithDescription(embed.Description)
                    .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                    .WithImageUrl(embed.Image?.Url)
                    .WithThumbnailUrl(embed.Thumbnail?.Url)
                    .WithTimestamp(embed.Timestamp ?? System.DateTimeOffset.UtcNow)
                    .WithTitle(embed.Title)
                    .WithUrl(embed.Url);

                // Copy fields if there are any
                foreach (var field in embed.Fields)
                {
                    eb.AddField(field.Name, field.Value, field.Inline);
                }

                await modNotesChannel.SendMessageAsync(embed: eb.Build());
            }
        }

        // Fallback: plain text (rare for MEE6)
        if (!isModerationLog && !string.IsNullOrWhiteSpace(message.Content))
            {
                string text = message.Content.ToLower();
                if (text.Contains("mute") ||
                    text.Contains("banned") ||
                    text.Contains("BAN") ||
                    text.Contains("kicked") ||
                    text.Contains("deafen") ||
                    text.Contains("warn"))
                {
                    await modChannel.SendMessageAsync(
                        $"📢 **Forwarded from Admin Logs:**\n{message.Content}"
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
