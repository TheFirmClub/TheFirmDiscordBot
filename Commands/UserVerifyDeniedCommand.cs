using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FirmDiscordBot.Commands;

public sealed class UserVerifyDeniedCommand : ISlashCommand
{
    public string Name => "userverifydenied";
    public string Description => "Deny verification (Senior Mods) + DM user + kick + close this manual verification ticket.";

    private const ulong ClosedManualVerifyCategoryId = 1474419760237379846UL;
    private const ulong ManualVerifyLogChannelId = 1474538794953867337UL;

    // Senior Mods only (and higher)
    private static readonly ulong[] AllowedRoleIds =
    {
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
            await command.FollowupAsync("⛔ This command is for Senior Moderators (and higher) only.", ephemeral: true);
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

        if (target != null)
        {
            // DM user
            try
            {
                var dmEmbed = new EmbedBuilder()
                    .WithTitle("❌ Verification Denied")
                    .WithColor(Color.Red)
                    .WithDescription(
                        "Your request to join **The Firm** Discord has been denied after review.\n\n" +
                        "If you believe this was a mistake, you may re-join at a later date and contact staff.\n\n" +
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

            // Kick
            try
            {
                await target.KickAsync($"Verification denied. Actioned by {invoker.Username} ({invoker.Id}).");
                kickSucceeded = true;
            }
            catch (Exception ex)
            {
                kickError = ex.Message;
            }
        }

        // Update status embed -> Denied
        if (hasStatusMsg)
        {
            try
            {
                var msg = await channel.GetMessageAsync(statusMsgId);
                if (msg is IUserMessage statusMessage)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("❌ Verification Status: Denied")
                        .WithColor(Color.Red)
                        .WithDescription(
                            target != null
                                ? $"{target.Mention} has been denied access.\n\nThis ticket is now closed."
                                : "User has been denied access.\n\nThis ticket is now closed."
                        )
                        .AddField("Status", "Denied ❌", true)
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
                    .WithTitle("❌ The Firm Verification Denied")
                    .WithColor(Color.Red)
                    .WithDescription("A verification ticket was denied by Senior Moderation and closed.")
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

        await channel.SendMessageAsync($"❌ Verification denied — actioned by {invoker.Mention}. Ticket closed.");

        await command.FollowupAsync(
            target != null
                ? $"✅ Denied {target.Mention}. DM: {(dmSent ? "sent ✅" : "failed ❌")} | Kick: {(kickSucceeded ? "success ✅" : "failed ❌")} | Ticket closed."
                : "✅ Denied user and closed ticket. (Owner not found in cache; DM/Kick not performed.)",
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