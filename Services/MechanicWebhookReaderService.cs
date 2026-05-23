using Discord;
using Discord.WebSocket;

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
                await HandleMoneyWithdraw(embed);
            }
            else if (text.Contains("Item Purchased", StringComparison.OrdinalIgnoreCase))
            {
                await HandleItemPurchased(embed);
            }
        }
    }

    private async Task HandleMoneyWithdraw(IEmbed embed)
    {
        var mechanic = GetFieldValue(embed, "Mechanic");
        var amount = GetFieldValue(embed, "Amount");

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

    private async Task HandleItemPurchased(IEmbed embed)
    {
        var player = GetFieldValue(embed, "Player");
        var mechanic = GetFieldValue(embed, "Mechanic");
        var item = GetFieldValue(embed, "Item");
        var quantity = GetFieldValue(embed, "Quantity");
        var totalCost = GetFieldValue(embed, "Total Cost");

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

    private string? GetFieldValue(IEmbed embed, string fieldName)
    {
        var field = embed.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));

        if (field.Equals(default(EmbedField)))
            return null;

        return field.Value.ToString().Trim();
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
}