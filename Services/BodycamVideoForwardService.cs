using Discord;
using Discord.WebSocket;
using System.Text.RegularExpressions;

public class BodycamVideoForwardService
{
    private readonly DiscordSocketClient _client;

    private const ulong SourceChannelId = 1411142575489810552UL;
    private const ulong TargetChannelId = 1437735072362139659UL;

    public BodycamVideoForwardService(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessage;
    }

    private async Task OnMessage(SocketMessage msg)
    {
        if (msg.Channel.Id != SourceChannelId)
            return;

        if (!msg.Author.IsWebhook)
            return;

        var content = msg.Content;

        if (string.IsNullOrWhiteSpace(content))
            return;

        // ✅ Ensure this is BODYCAM metadata from FiveManage
        if (!content.Contains("\"name\": \"Bodycam Recording Clips\"", StringComparison.OrdinalIgnoreCase))
            return;

        var link = ExtractVideoLink(content);

        if (link == null)
        {
            Console.WriteLine("❌ No video link detected.");
            return;
        }

        var channel = _client.GetChannel(TargetChannelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("❌ Target channel not found.");
            return;
        }

        Console.WriteLine($"🎥 Bodycam clip detected: {link}");

        // Send link first (so Discord embeds the video)
        await channel.SendMessageAsync(link);

        var embed = new EmbedBuilder()
            .WithColor(Color.DarkBlue)
            .WithTitle("🎥 Bodycam Footage Uploaded")
            .AddField("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            .WithFooter("Evidence System")
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private string? ExtractVideoLink(string text)
    {
        var match = Regex.Match(text, @"https?:\/\/[^\s]+\.webm", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }
}