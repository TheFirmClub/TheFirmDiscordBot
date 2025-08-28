using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _mee6LogChannelId;  // MEE6 Admin Log channel
    private readonly ulong _modNotesChannelId; // Your Mod Notes channel

    public Mee6LogForwarder(DiscordSocketClient client, ulong mee6LogChannelId, ulong modNotesChannelId)
    {
        _client = client;
        _mee6LogChannelId = mee6LogChannelId;
        _modNotesChannelId = modNotesChannelId;

        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            if (message.Channel.Id != _mee6LogChannelId) return; // Only watch MEE6 log channel
            if (!message.Author.IsBot) return;                   // Only forward bot logs (MEE6)

            var modChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modChannel == null) return;

            bool isModerationLog = false;

            foreach (var embed in message.Embeds)
            {
                string combinedText = $"{embed.Title} {embed.Description} " +
                                      string.Join(" ", embed.Fields.Select(f => f.Name + " " + f.Value));

                combinedText = combinedText.ToLower();

                if (combinedText.Contains("mute") ||
                    combinedText.Contains("banned") ||
                    combinedText.Contains("BAN") ||
                    combinedText.Contains("kicked") ||
                    combinedText.Contains("deafen") ||
                    combinedText.Contains("warn"))
                {
                    isModerationLog = true;

                    // Copy embed to a new builder so we can add moderator info
                    var embedBuilder = embed.ToEmbedBuilder();

                    // Try to extract moderator info
                    string? moderator = null;

                    // From fields
                    var modField = embed.Fields.FirstOrDefault(f =>
                        f.Name.ToLower().Contains("moderator") ||
                        f.Value.ToLower().Contains("moderator"));

                    if (modField.Name != null)
                        moderator = $"{modField.Value}";

                    // From footer
                    if (string.IsNullOrEmpty(moderator) && embed.Footer.HasValue)
                        moderator = embed.Footer.Value.Text;

                    // From description
                    if (string.IsNullOrEmpty(moderator) && !string.IsNullOrWhiteSpace(embed.Description))
                    {
                        var desc = embed.Description.ToLower();
                        if (desc.Contains("by "))
                        {
                            int idx = desc.LastIndexOf("by ");
                            moderator = embed.Description.Substring(idx + 3).Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(moderator))
                    {
                        embedBuilder.AddField("👮 Moderator", moderator, inline: false);
                    }

                    await modChannel.SendMessageAsync(embed: embedBuilder.Build());
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
