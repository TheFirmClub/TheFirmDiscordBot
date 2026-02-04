using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class EvidenceRelayService
{
    private readonly DiscordSocketClient _client;

    private const ulong SourceChannelId = 1400632503915774082UL;
    private const ulong TargetChannelId = 1468006426151616686UL;

    // Matches: transferred from "LSPD_evidence_1"
    private static readonly Regex EvidenceTransferRegex =
        new(@"transferred\s+from\s+""LSPD_evidence_\d+""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public EvidenceRelayService(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceived;
        _client.Ready += OnReady;
    }

    private Task OnReady()
    {
        Console.WriteLine("EvidenceRelayService is ready.");
        return Task.CompletedTask;
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        try
        {
            // Ignore self messages
            if (msg.Author.Id == _client.CurrentUser.Id)
                return;

            // Only monitor source channel
            if (msg.Channel.Id != SourceChannelId)
                return;

            // Must contain embeds
            if (msg.Embeds == null || msg.Embeds.Count == 0)
                return;

            // Check if any embed contains valid transfer text
            bool containsEvidenceTransfer =
                msg.Embeds.Any(embed => EmbedContainsEvidenceTransfer(embed));

            if (!containsEvidenceTransfer)
                return;

            var targetChannel = _client.GetChannel(TargetChannelId) as IMessageChannel;

            if (targetChannel == null)
            {
                Console.WriteLine("EvidenceRelay: Target channel not found.");
                return;
            }

            // Forward embeds and message content
            await targetChannel.SendMessageAsync(
                text: msg.Content,
                embeds: msg.Embeds.ToArray()
            );

            Console.WriteLine($"Evidence relay forwarded message {msg.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EvidenceRelay error: {ex}");
        }
    }

    private bool EmbedContainsEvidenceTransfer(Embed embed)
    {
        bool Check(string? s) =>
            !string.IsNullOrEmpty(s) &&
            EvidenceTransferRegex.IsMatch(s);

        if (Check(embed.Title)) return true;
        if (Check(embed.Description)) return true;
        if (Check(embed.Author?.Name)) return true;
        if (Check(embed.Footer?.Text)) return true;

        if (embed.Fields != null)
        {
            foreach (var f in embed.Fields)
            {
                if (Check(f.Name) || Check(f.Value))
                    return true;
            }
        }

        return false;
    }
}
