using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FirmDiscordBot.Commands;

public sealed class UserVerifiedCommand : ISlashCommand
{
    public string Name => "userverified";
    public string Description => "Verify ticket owner + close this manual verification ticket.";

    private const ulong ClosedManualVerifyCategoryId = 1474419760237379846UL;

    private const ulong VerifiedRoleId = 1393664929042530425UL;
    private const ulong ManualVerifyRoleId = 1474419863798812752UL;
    private const ulong NonVerifiedRoleId = 1393664929042530425UL;

    // ✅ NEW: Manual Verification log channel
    private const ulong ManualVerifyLogChannelId = 1474538794953867337UL;
    private const ulong FirmRobotRoleId = 1398764987685539962UL;
    private const ulong TheFirmRoleId = 1508057487863971843UL;

    private static readonly ulong[] StaffRoleIds =
    {
        1393729574537396355UL,
        1393623589122736238UL,
        1393638449709584434UL,
        1405330877440983130UL,
        1393728468608487594UL,
        1393590761953558608UL
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        // ✅ Must respond within 3 seconds
        await command.DeferAsync(ephemeral: true);

        if (command.User is not SocketGuildUser invoker)
        {
            await command.FollowupAsync("❌ This command must be used in a server.", ephemeral: true);
            return;
        }

        if (!invoker.Roles.Any(r => StaffRoleIds.Contains(r.Id)))
        {
            await command.FollowupAsync("⛔ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.FollowupAsync("❌ This must be run inside a manual verification ticket channel.", ephemeral: true);
            return;
        }

        var topic = channel.Topic ?? "";
        if (!topic.Contains("type:new_verify", StringComparison.OrdinalIgnoreCase))
        {
            await command.FollowupAsync("❌ This channel is not a manual verification ticket (`type:new_verify` missing in topic).", ephemeral: true);
            return;
        }

        if (!TryParseUlongToken(topic, "owner", out var ownerId))
        {
            await command.FollowupAsync("❌ Could not find `owner:<id>` in the channel topic.", ephemeral: true);
            return;
        }

        var hasStatusMsg = TryParseUlongToken(topic, "statusmsg", out var statusMsgId);

        var guild = invoker.Guild;

        // Cached user (avoids IGuild.GetUserAsync explicit interface issues)
        var target = guild.GetUser(ownerId);

        bool rolesApplied = false;
        string roleWarning = "";

        if (target != null)
        {
            var verifiedRole = guild.GetRole(VerifiedRoleId);
            var manualRole = guild.GetRole(ManualVerifyRoleId);
            var nonVerifiedRole = guild.GetRole(NonVerifiedRoleId);

            if (verifiedRole == null)
            {
                await command.FollowupAsync("❌ Verified role not found.", ephemeral: true);
                return;
            }

            // Add Verified
            await target.AddRoleAsync(verifiedRole);

            // Remove Manual Verify (ignore if missing)
            if (manualRole != null)
            {
                try { await target.RemoveRoleAsync(manualRole); }
                catch { /* ignore */ }
            }

            // Remove Non-Verified (MEE6 role) — may fail if hierarchy is wrong
            if (nonVerifiedRole != null)
            {
                try
                {
                    await target.RemoveRoleAsync(nonVerifiedRole);
                }
                catch (Exception ex)
                {
                    roleWarning =
                        $"⚠️ I could not remove the **Non-Verified** role automatically. " +
                        $"This is usually a **role hierarchy** issue (bot role must be above Non-Verified). ({ex.Message})";
                }
            }

            rolesApplied = true;
        }

        // ✅ Update the status embed -> Verified
        if (hasStatusMsg)
        {
            try
            {
                var msg = await channel.GetMessageAsync(statusMsgId);
                if (msg is IUserMessage statusMessage)
                {
                    var verifiedEmbed = new EmbedBuilder()
                        .WithTitle("✅ Verification Status: Verified")
                        .WithColor(Color.Green)
                        .WithDescription(
                            rolesApplied && target != null
                                ? $"{target.Mention} has been verified.\n\nThis ticket is now closed."
                                : "Verification has been completed.\n\nThis ticket is now closed."
                        )
                        .AddField("Status", "Verified ✅", true)
                        .AddField("Verified By", invoker.Mention, true)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await statusMessage.ModifyAsync(m => m.Embed = verifiedEmbed);
                }
            }
            catch
            {
                // ignore — still close ticket
            }
        }

        // ✅ Log verification to the Manual Verify log channel (new embed)
        try
        {
            var logChannel = guild.GetTextChannel(ManualVerifyLogChannelId);
            if (logChannel != null)
            {
                var logEmbed = new EmbedBuilder()
                    .WithTitle("✅ Manual Verification Completed")
                    .WithColor(Color.Green)
                    .WithDescription(
                        "A manual verification ticket has been completed and closed."
                    )
                    .AddField("User",
                        target != null ? $"{target.Mention} (`{target.Id}`)" : $"`{ownerId}` (not cached)",
                        true)
                    .AddField("Verified By", $"{invoker.Mention} (`{invoker.Id}`)", true)
                    .AddField("Ticket", $"{channel.Name} (`{channel.Id}`)", false)
                    .AddField("Outcome",
                        string.IsNullOrWhiteSpace(roleWarning)
                            ? "Verified role applied. Manual Verify + Non-Verified removed."
                            : "Verified role applied, but Non-Verified could not be removed automatically.",
                        false)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: logEmbed);

                if (!string.IsNullOrWhiteSpace(roleWarning))
                    await logChannel.SendMessageAsync($"⚠️ {roleWarning}\nTicket: <#{channel.Id}>");
            }
        }
        catch
        {
            // Logging failure should not stop ticket closure
        }

        await EnsureManualVerifyAccessAsync(channel);

        // Close ticket: rename + move category
        var newName = channel.Name.StartsWith("closed-", StringComparison.OrdinalIgnoreCase)
            ? channel.Name
            : $"closed-{channel.Name}";

        await channel.ModifyAsync(props =>
        {
            props.CategoryId = ClosedManualVerifyCategoryId;
            props.Name = newName;
        });

        // Send a visible note in-channel (optional but helpful)
        if (!string.IsNullOrWhiteSpace(roleWarning))
            await channel.SendMessageAsync(roleWarning);

        await channel.SendMessageAsync($"✅ Ticket closed by {command.User.Mention}.");

        // ✅ Final interaction response
        if (target != null)
        {
            await command.FollowupAsync(
                $"✅ Verified {target.Mention}, updated status, and closed the ticket." +
                (string.IsNullOrWhiteSpace(roleWarning) ? "" : "\n\n" + roleWarning),
                ephemeral: true);
        }
        else
        {
            await command.FollowupAsync(
                "✅ Updated status and closed the ticket. (Ticket owner not found in cache; roles may not have been applied.)",
                ephemeral: true);
        }
    }


    private static async Task EnsureManualVerifyAccessAsync(SocketTextChannel channel)
    {
        var firmRobotRole = channel.Guild.GetRole(FirmRobotRoleId);
        if (firmRobotRole != null)
        {
            await channel.AddPermissionOverwriteAsync(firmRobotRole,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow,
                    manageMessages: PermValue.Allow,
                    manageChannel: PermValue.Allow,
                    useApplicationCommands: PermValue.Allow));
        }
        else
        {
            Console.WriteLine("⚠️ Firm Robot role not found while closing manual verification ticket.");
        }

        var theFirmRole = channel.Guild.GetRole(TheFirmRoleId);
        if (theFirmRole != null)
        {
            await channel.AddPermissionOverwriteAsync(theFirmRole,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow));
        }
        else
        {
            Console.WriteLine("⚠️ The Firm role not found while closing manual verification ticket.");
        }
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