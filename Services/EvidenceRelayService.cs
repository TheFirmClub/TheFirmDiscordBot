using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class EvidenceRelayService
{
    private readonly DiscordSocketClient _client;

    private const ulong SourceChannelId = 1400632503915774082UL;
    private const ulong TargetChannelId = 1468006426151616686UL;

    private const string EvidenceKeyword = "LSPD_evidence_";

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
            if (msg.Author.Id == _client.CurrentUser.Id) return;
            if (msg.Channel.Id != SourceChannelId) return;

            // Must contain embeds
            if (msg.Embeds == null || msg.Embeds.Count == 0) return;

            bool containsEvidence = msg.Embeds.Any(embed =>
                EmbedContainsEvidence(embed));

            if (!containsEvidence) return;

            var targetChannel = _client.GetChannel(TargetChannelId) as IMessageChannel;

            if (targetChannel == null)
            {
                Console.WriteLine("EvidenceRelay: Target channel not found.");
                return;
            }

            // Forward ALL embeds exactly as they are
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

    private bool EmbedContainsEvidence(Embed embed)
    {
        bool Check(string? s) =>
            !string.IsNullOrEmpty(s) &&
            s.Contains(EvidenceKeyword, StringComparison.OrdinalIgnoreCase);

        // Check common embed locations
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
