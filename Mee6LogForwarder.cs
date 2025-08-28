@ -1,107 + 1,86 @@
﻿using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Mee6LogForwarder
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _adminChannelId = 1393597248495030272;   // Admin Logs
    private readonly ulong _modNotesChannelId = 1394451583709745273; // Mod Notes
    private readonly ulong _mee6Id = 1393611163853656085;            // MEE6-TheFirm Bot ID

    // List of moderation keywords to filter
    private readonly string[] _moderationKeywords = new[]
    {
        "[MUTE]", "[UNMUTE]", "[BAN]", "[KICK]", "[WARN]", "[DEAFEN]", "[UNDEAFEN]"
    };

    public Mee6LogForwarder(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            if (message is not SocketUserMessage msg)
                return;

            // Only forward messages from the Admin Logs channel
            // Only forward from Admin Logs channel
            if (msg.Channel.Id != _adminChannelId)
                return;

            // Only forward messages from MEE6-TheFirm
            // Only forward if it's from MEE6 (The Firm bot)
            if (msg.Author.Id != _mee6Id)
                return;

            // Get the Mod Notes channel
            // Get Mod Notes channel
            var modNotesChannel = _client.GetChannel(_modNotesChannelId) as IMessageChannel;
            if (modNotesChannel == null)
                return;

            bool forwarded = false;

            // Forward moderation embeds
            // Forward embeds if they contain moderation keywords
            if (msg.Embeds.Count > 0)
            {
                foreach (var embed in msg.Embeds)
                {
                    string titleOrDesc = (embed.Title ?? "") + " " + (embed.Description ?? "");

                    bool isModeration = false;
                    foreach (var keyword in _moderationKeywords)
                    {
                        if (titleOrDesc.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            isModeration = true;
                            break;
                        }
                    }
                    bool isModerationLog = false;

                    if (!isModeration)
                        continue; // Skip non-moderation embeds
                    // Check title & description for keywords
                    if (!string.IsNullOrEmpty(embed.Title))
                        isModerationLog |= _moderationKeywords.Any(k => embed.Title.Contains(k, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(embed.Description))
                        isModerationLog |= _moderationKeywords.Any(k => embed.Description.Contains(k, StringComparison.OrdinalIgnoreCase));

                    var eb = new EmbedBuilder()
                        .WithAuthor(embed.Author?.Name, embed.Author?.IconUrl, embed.Author?.Url)
                        .WithTitle(embed.Title)
                        .WithDescription(embed.Description)
                        .WithColor(embed.Color ?? Color.Blue)
                        .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                        .WithThumbnailUrl(embed.Thumbnail?.Url)
                        .WithImageUrl(embed.Image?.Url)
                        .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow)
                        .WithUrl(embed.Url);
                    if (isModerationLog)
                    {
                        var eb = new EmbedBuilder()
                            .WithAuthor(embed.Author?.Name, embed.Author?.IconUrl, embed.Author?.Url)
                            .WithTitle(embed.Title)
                            .WithDescription(embed.Description)
                            .WithColor(embed.Color ?? Color.Blue)
                            .WithFooter(embed.Footer?.Text, embed.Footer?.IconUrl)
                            .WithThumbnailUrl(embed.Thumbnail?.Url)
                            .WithImageUrl(embed.Image?.Url)
                            .WithTimestamp(embed.Timestamp ?? DateTimeOffset.UtcNow);

                        foreach (var field in embed.Fields)
                            eb.AddField(field.Name, field.Value, field.Inline);
                        // Copy fields
                        foreach (var field in embed.Fields)
                            eb.AddField(field.Name, field.Value, field.Inline);

                        await modNotesChannel.SendMessageAsync(embed: eb.Build());
                        forwarded = true;
                    }
                }

                // Fallback: forward plain-text moderation messages
                if (!forwarded && !string.IsNullOrWhiteSpace(msg.Content))
                {
                    foreach (var keyword in _moderationKeywords)
                    {
                        if (msg.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            await modNotesChannel.SendMessageAsync(
                                $"📢 **Forwarded from Admin Logs:**\n{msg.Content}"
                            );
                            break;
                            await modNotesChannel.SendMessageAsync(embed: eb.Build());
                        }
                    }
                }
            }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error forwarding MEE6 log: {ex.Message}");
        }
    }
}
