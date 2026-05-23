using Discord;
using Discord.WebSocket;
using System.Text.RegularExpressions;

public class MechanicWebhookReaderService
{
    private readonly DiscordSocketClient _client;

    private const ulong SourceChannelId = 1472854747353448478UL;
    private const ulong AlertChannelId = 1507709867039916052UL;

    public MechanicWebhookReaderService(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceived;
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        if (msg.Channel.Id != SourceChannelId)
            return;

        if (!msg.Author.IsWebhook)
            return;

        if (msg.Embeds.Count == 0)
            return;

        foreach (var embed in msg.Embeds)
        {
            var text = BuildEmbedText(embed);

            if (!text.Contains("Redline", StringComparison.OrdinalIgnoreCase))
                continue;

            if (text.Contains("Money Withdraw", StringComparison.OrdinalIgnoreCase))
            {
                await HandleMoneyWithdraw(embed, text);
            }
            else if (text.Contains("Item Purchased", StringComparison.OrdinalIgnoreCase))
            {
                await HandleItemPurchased(embed, text);
            }
        }
    }

    private async Task HandleMoneyWithdraw(IEmbed embed, string text)
    {
        var mechanic = ExtractValue(text, "Mechanic");
        var amount = ExtractValue(text, "Amount");

        var channel = _client.GetChannel(AlertChannelId) as IMessageChannel;
        if (channel == null) return;

        var alert = new EmbedBuilder()
            .WithTitle("💸 Mechanic Money Withdrawn")
            .WithColor(Color.Red)
            .AddField("Mechanic", mechanic ?? "Unknown", true)
            .AddField("Amount Withdrawn", amount ?? "Unknown", true)
            .WithTimestamp(DateTimeOffset.Now)
            .Build();

        await channel.SendMessageAsync(embed: alert);
    }

    private async Task HandleItemPurchased(IEmbed embed, string text)
    {
        var player = ExtractValue(text, "Player");
        var mechanic = ExtractValue(text, "Mechanic");
        var item = ExtractValue(text, "Item");
        var quantity = ExtractValue(text, "Quantity");
        var totalCost = ExtractValue(text, "Total Cost");

        var channel = _client.GetChannel(AlertChannelId) as IMessageChannel;
        if (channel == null) return;

        var alert = new EmbedBuilder()
            .WithTitle("🛒 Mechanic Item Purchased")
            .WithColor(Color.Green)
            .AddField("Player", player ?? "Unknown", true)
            .AddField("Mechanic", mechanic ?? "Unknown", true)
            .AddField("Item", item ?? "Unknown", true)
            .AddField("Quantity", quantity ?? "Unknown", true)
            .AddField("Total Cost", totalCost ?? "Unknown", true)
            .WithTimestamp(DateTimeOffset.Now)
            .Build();

        await channel.SendMessageAsync(embed: alert);
    }

    private string BuildEmbedText(IEmbed embed)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(embed.Title))
            parts.Add(embed.Title);

        if (!string.IsNullOrWhiteSpace(embed.Description))
            parts.Add(embed.Description);

        foreach (var field in embed.Fields)
        {
            parts.Add($"{field.Name}: {field.Value}");
        }

        return CleanDiscordMarkdown(string.Join("\n", parts));
    }

    private string CleanDiscordMarkdown(string text)
    {
        return text.Replace("**", "")
                   .Replace("__", "")
                   .Replace("*", "")
                   .Replace("`", "");
    }

    private string? ExtractValue(string text, string fieldName)
    {
        var pattern = $@"{Regex.Escape(fieldName)}\s*:?\s*(.+)";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        return match.Groups[1].Value.Trim();
    }
}