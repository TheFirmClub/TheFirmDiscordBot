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
        Console.WriteLine("GLOBAL MESSAGE EVENT");
        Console.WriteLine($"Channel: {msg.Channel.Id}");
        Console.WriteLine($"Author: {msg.Author.Username}");
        Console.WriteLine($"IsWebhook: {msg.Author.IsWebhook}");
        Console.WriteLine($"Content: {msg.Content}");
        Console.WriteLine($"Embeds: {msg.Embeds.Count}");
        Console.WriteLine("--------------------------------------------------");

        if (msg.Channel.Id != SourceChannelId)
        {
            Console.WriteLine("❌ Ignoring — wrong channel.");
            return;
        }

        Console.WriteLine("✅ Correct source channel");

        if (!msg.Author.IsWebhook)
        {
            Console.WriteLine("❌ Not a webhook message");
            return;
        }

        Console.WriteLine("✅ Webhook detected");

        string fullText = msg.Content ?? "";

        // Read embed text too
        foreach (var embed in msg.Embeds)
        {
            Console.WriteLine("🔎 Reading embed...");

            if (!string.IsNullOrEmpty(embed.Title))
                fullText += "\n" + embed.Title;

            if (!string.IsNullOrEmpty(embed.Description))
                fullText += "\n" + embed.Description;

            foreach (var field in embed.Fields)
            {
                fullText += "\n" + field.Name + "\n" + field.Value;
            }
        }

        Console.WriteLine("📜 Combined Message Text:");
        Console.WriteLine(fullText);

        // Check metadata
        if (!fullText.Contains("Bodycam Recording Clips", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("❌ Not a bodycam recording clip");
            return;
        }

        Console.WriteLine("✅ Bodycam metadata detected");

        var link = ExtractVideoLink(fullText);

        if (link == null)
        {
            Console.WriteLine("❌ Video link not found");
            return;
        }

        Console.WriteLine($"✅ Video link: {link}");

        var channel = _client.GetChannel(TargetChannelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("❌ Target channel not found");
            return;
        }

        Console.WriteLine("🚀 Forwarding video...");

        await channel.SendMessageAsync(link);

        var embedMessage = new EmbedBuilder()
            .WithColor(Color.DarkBlue)
            .WithTitle("🎥 Bodycam Footage Uploaded")
            .AddField("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            .WithFooter("Evidence System")
            .Build();

        await channel.SendMessageAsync(embed: embedMessage);

        Console.WriteLine("✅ Video forwarded successfully");
    }

    private string? ExtractVideoLink(string text)
    {
        Console.WriteLine("🔍 Searching for video link...");

        var match = Regex.Match(text, @"https?:\/\/[^\s]+\.webm", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            Console.WriteLine("✅ Found .webm link");
            return match.Value;
        }

        Console.WriteLine("❌ No .webm link detected");
        return null;
    }
}