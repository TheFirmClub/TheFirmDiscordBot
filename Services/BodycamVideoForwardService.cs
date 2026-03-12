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

        // Ensure it's from FiveManage webhook
        if (!msg.Author.Username.Equals("Mobile Media", StringComparison.OrdinalIgnoreCase) &&
            !msg.Author.Username.Equals("Fivemanage", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(msg.Content))
            return;

        // Ensure it's bodycam metadata
        if (!msg.Content.Contains("\"name\": \"Bodycam Recording Clips\"", StringComparison.OrdinalIgnoreCase))
            return;

        var link = ExtractVideoLink(msg.Content);

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

        Console.WriteLine($"🎥 Forwarding bodycam clip: {link}");

        // Send link first so video embeds
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