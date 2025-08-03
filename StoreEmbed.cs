using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class StoreEmbed
{
    // === Configuration ===
    private const ulong StoreChannelId = 1393596336938553478;
    private const string StoreUrl = "https://store.thefirm.club";
    private const string EmbedTitle = "🛍️ Visit our Store Today!";
    private const string LogoUrl = "https://live.staticflickr.com/65535/54657187788_1731580108_s.jpg";               // Replace with your logo URL
    private const string BannerImageUrl = "https://live.staticflickr.com/65535/54696015747_d25f2a5d61.jpg"; // Replace with promo/banner image

    private readonly DiscordSocketClient _client;

    public StoreEmbed(DiscordSocketClient client)
    {
        _client = client;
        _client.Ready += OnBotReady;
    }

    private async Task OnBotReady()
    {
        try
        {
            var channel = _client.GetChannel(StoreChannelId) as IMessageChannel;
            if (channel == null)
            {
                Console.WriteLine("[StoreEmbed] Channel not found.");
                return;
            }

            var messages = await channel.GetMessagesAsync(10).FlattenAsync();
            bool alreadyExists = messages.Any(m =>
                m.Author.Id == _client.CurrentUser.Id &&
                m.Embeds.Any(e =>
                    e.Title == EmbedTitle &&
                    e.Description != null &&
                    e.Description.Contains(StoreUrl)));

            if (alreadyExists)
            {
                Console.WriteLine("[StoreEmbed] Message already exists. Skipping.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle(EmbedTitle)
                .WithDescription($@"
🌟 **Support the server, unlock exclusive perks, and enhance your roleplay experience!**

🔗 **[Click here to visit the store!]({StoreUrl})**

🎁 Every purchase helps us improve the server for the entire community.
")
                .WithImageUrl(BannerImageUrl)
                .WithColor(new Color(0x7289DA))
                .WithFooter(footer =>
                {
                    footer.Text = "The Firm";
                    footer.IconUrl = LogoUrl;
                })
                .WithCurrentTimestamp()
                .Build();

            await channel.SendMessageAsync(embed: embed);
            Console.WriteLine("[StoreEmbed] Fancy embed sent.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StoreEmbed] Error: {ex.Message}");
        }
    }
}
