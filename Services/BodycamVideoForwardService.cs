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

        string fullText = msg.Content ?? "";

        foreach (var embed in msg.Embeds)
        {
            if (!string.IsNullOrEmpty(embed.Title))
                fullText += "\n" + embed.Title;

            if (!string.IsNullOrEmpty(embed.Description))
                fullText += "\n" + embed.Description;

            foreach (var field in embed.Fields)
                fullText += "\n" + field.Name + "\n" + field.Value;
        }

        if (!fullText.Contains("Bodycam Recording Clips", StringComparison.OrdinalIgnoreCase))
            return;

        var link = ExtractVideoLink(fullText);

        if (link == null)
            return;

        var channel = _client.GetChannel(TargetChannelId) as IMessageChannel;

        if (channel == null)
            return;

        await channel.SendMessageAsync(link);

        var embedMessage = new EmbedBuilder()
            .WithColor(Color.DarkBlue)
            .WithTitle("🎥 Bodycam Footage Uploaded")
            .AddField("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            .WithFooter("The Firm Development")
            .Build();

        await channel.SendMessageAsync(embed: embedMessage);
    }

    private string? ExtractVideoLink(string text)
    {
        var match = Regex.Match(text, @"https?:\/\/[^\s]+\.webm", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }
}