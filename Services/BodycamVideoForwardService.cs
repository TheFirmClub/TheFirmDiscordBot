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
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"GLOBAL MESSAGE EVENT");
        Console.WriteLine($"Channel: {msg.Channel.Id}");
        Console.WriteLine($"Author: {msg.Author.Username}");
        Console.WriteLine($"IsWebhook: {msg.Author.IsWebhook}");
        Console.WriteLine("Content:");
        Console.WriteLine(msg.Content);
        Console.WriteLine("--------------------------------------------------");

        // Only process source channel
        if (msg.Channel.Id != SourceChannelId)
        {
            Console.WriteLine("❌ Ignoring — wrong channel.");
            return;
        }

        Console.WriteLine("✅ Correct source channel.");

        // Check webhook username (FiveManage / Mobile Media)
        if (!msg.Author.Username.Equals("Mobile Media", StringComparison.OrdinalIgnoreCase) &&
            !msg.Author.Username.Equals("Fivemanage", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("❌ Ignoring — not FiveManage webhook.");
            return;
        }

        Console.WriteLine("✅ Message from FiveManage.");

        if (string.IsNullOrWhiteSpace(msg.Content))
        {
            Console.WriteLine("❌ Message content empty.");
            return;
        }

        // Check metadata
        if (!msg.Content.Contains("\"name\": \"Bodycam Recording Clips\"", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("❌ Metadata does not match Bodycam Recording Clips.");
            return;
        }

        Console.WriteLine("✅ Bodycam metadata detected.");

        // Extract video link
        var link = ExtractVideoLink(msg.Content);

        if (link == null)
        {
            Console.WriteLine("❌ No video link found.");
            return;
        }

        Console.WriteLine($"✅ Video link detected: {link}");

        var channel = _client.GetChannel(TargetChannelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("❌ Target channel not found.");
            return;
        }

        Console.WriteLine("🚀 Forwarding video...");

        // Send link first so Discord embeds video
        await channel.SendMessageAsync(link);

        var embed = new EmbedBuilder()
            .WithColor(Color.DarkBlue)
            .WithTitle("🎥 Bodycam Footage Uploaded")
            .AddField("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            .WithFooter("Evidence System")
            .Build();

        await channel.SendMessageAsync(embed: embed);

        Console.WriteLine("✅ Bodycam video forwarded successfully.");
    }

    private string? ExtractVideoLink(string text)
    {
        Console.WriteLine("🔍 Searching for video link...");

        var match = Regex.Match(text, @"https?:\/\/[^\s]+\.webm", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            Console.WriteLine("✅ Regex matched video URL.");
            return match.Value;
        }

        Console.WriteLine("❌ Regex failed to find .webm link.");
        return null;
    }
}