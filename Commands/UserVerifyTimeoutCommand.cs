using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FirmDiscordBot.Commands;

public sealed class UserVerifyTimeoutCommand : ISlashCommand
{
    public string Name => "userverifytimeout";
    public string Description => "Kick ticket owner for no response (24h timeout) + close this manual verification ticket.";

    private const ulong ClosedManualVerifyCategoryId = 1474419760237379846UL;
    private const ulong ManualVerifyLogChannelId = 1474538794953867337UL;

    // Roles allowed to run this (Discord Mods + higher)
    private const ulong DiscordModeratorRoleId = 1393623589122736238UL;
    private static readonly ulong[] AllowedRoleIds =
    {
        1393623589122736238UL, // Discord Moderator
        1393638449709584434UL, // Senior Moderator
        1405330877440983130UL, // Assistant Head Moderator
        1393728468608487594UL, // Head Moderator
        1393590761953558608UL  // Senior Management
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (command.User is not SocketGuildUser invoker)
        {
            await command.FollowupAsync("❌ This command must be used in a server.", ephemeral: true);
            return;
        }

        if (!invoker.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await command.FollowupAsync("⛔ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.FollowupAsync("❌ This must be run inside a verification ticket channel.", ephemeral: true);
            return;
        }

        var topic = channel.Topic ?? "";
        if (!topic.Contains("type:new_verify", StringComparison.OrdinalIgnoreCase))
        {
            await command.FollowupAsync("❌ This channel is not a verification ticket (`type:new_verify` missing in topic).", ephemeral: true);
            return;
        }

        if (!TryParseUlongToken(topic, "owner", out var ownerId))
        {
            await command.FollowupAsync("❌ Could not find `owner:<id>` in the channel topic.", ephemeral: true);
            return;
        }

        var hasStatusMsg = TryParseUlongToken(topic, "statusmsg", out var statusMsgId);

        var guild = invoker.Guild;
        var target = guild.GetUser(ownerId);

        bool dmSent = false;
        string dmError = "";
        bool kickSucceeded = false;
        string kickError = "";

        // DM + Kick (best effort)
        if (target != null)
        {
            try
            {
                var dmEmbed = new EmbedBuilder()
                    .WithTitle("⏰ Verification Timeout")
                    .WithColor(Color.Orange)
                    .WithDescription(
                        "You were removed from **The Firm** Discord because we did not receive a response to your verification in time (24 hours).\n\n" +
                        "You can re-join when you are ready and complete verification again.\n\n" +
                        "Web:\nhttps://thefirm.club\n" +
                        "Discord:\nhttps://discord.thefirm.club"
                    )
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await target.SendMessageAsync(embed: dmEmbed);
                dmSent = true;
            }
            catch (Exception ex)
            {
                dmError = ex.Message;
            }

            try
            {
                await target.KickAsync($"Verification timeout (24h no response). Actioned by {invoker.Username} ({invoker.Id}).");
                kickSucceeded = true;
            }
            catch (Exception ex)
            {
                kickError = ex.Message;
            }
        }

        // Update status embed -> Timed Out
        if (hasStatusMsg)
        {
            try
            {
                var msg = await channel.GetMessageAsync(statusMsgId);
                if (msg is IUserMessage statusMessage)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("⏰ Verification Status: Timed Out")
                        .WithColor(Color.Orange)
                        .WithDescription(
                            target != null
                                ? $"{target.Mention} did not respond within 24 hours.\n\nThis ticket is now closed."
                                : "User did not respond within 24 hours.\n\nThis ticket is now closed."
                        )
                        .AddField("Status", "Timed Out ⏰", true)
                        .AddField("Actioned By", invoker.Mention, true)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await statusMessage.ModifyAsync(m => m.Embed = embed);
                }
            }
            catch { /* ignore */ }
        }

        // Log
        try
        {
            var logChannel = guild.GetTextChannel(ManualVerifyLogChannelId);
            if (logChannel != null)
            {
                var outcome =
                    $"DM Sent: {(dmSent ? "Yes ✅" : "No ❌")}\n" +
                    $"Kick: {(kickSucceeded ? "Yes ✅" : "No ❌")}";

                var logEmbed = new EmbedBuilder()
                    .WithTitle("⏰ The Firm Verification Timed Out")
                    .WithColor(Color.Orange)
                    .WithDescription("A verification ticket was closed due to no response (24h).")
                    .AddField("User", target != null ? $"{target.Mention} (`{target.Id}`)" : $"`{ownerId}` (not cached)", true)
                    .AddField("Actioned By", $"{invoker.Mention} (`{invoker.Id}`)", true)
                    .AddField("Ticket", $"{channel.Name} (`{channel.Id}`)", false)
                    .AddField("Outcome", outcome, false)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: logEmbed);

                if (!string.IsNullOrWhiteSpace(dmError))
                    await logChannel.SendMessageAsync($"⚠️ DM failed: `{dmError}`\nTicket: <#{channel.Id}>");

                if (!string.IsNullOrWhiteSpace(kickError))
                    await logChannel.SendMessageAsync($"⚠️ Kick failed: `{kickError}`\nTicket: <#{channel.Id}>");
            }
        }
        catch { /* ignore */ }

        // Close ticket (move + rename)
        var newName = channel.Name.StartsWith("closed-", StringComparison.OrdinalIgnoreCase)
            ? channel.Name
            : $"closed-{channel.Name}";

        await channel.ModifyAsync(props =>
        {
            props.CategoryId = ClosedManualVerifyCategoryId;
            props.Name = newName;
        });

        await channel.SendMessageAsync($"⏰ Verification timed out (24h) — actioned by {invoker.Mention}. Ticket closed.");

        await command.FollowupAsync(
            target != null
                ? $"✅ Timed out {target.Mention}. DM: {(dmSent ? "sent ✅" : "failed ❌")} | Kick: {(kickSucceeded ? "success ✅" : "failed ❌")} | Ticket closed."
                : "✅ Timed out user and closed ticket. (Owner not found in cache; DM/Kick not performed.)",
            ephemeral: true);
    }

    private static bool TryParseUlongToken(string topic, string key, out ulong value)
    {
        value = 0;

        var parts = topic.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var token = parts.FirstOrDefault(p => p.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase));
        if (token == null) return false;

        var str = token[(key.Length + 1)..].Trim();
        return ulong.TryParse(str, out value);
    }
}