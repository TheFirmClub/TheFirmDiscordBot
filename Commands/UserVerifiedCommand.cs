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

    private const ulong ClosedManualVerifyCategoryId = 1474419760237379846;
    private const ulong VerifiedRoleId = 1393625125257089135;
    private const ulong ManualVerifyRoleId = 1474419863798812752;

    private static readonly ulong[] StaffRoleIds =
    {
        1393729574537396355,
        1393623589122736238,
        1393638449709584434,
        1405330877440983130,
        1393728468608487594,
        1393590761953558608
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser invoker)
        {
            await command.RespondAsync("❌ Could not resolve your guild user.", ephemeral: true);
            return;
        }

        if (!invoker.Roles.Any(r => StaffRoleIds.Contains(r.Id)))
        {
            await command.RespondAsync("⛔ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.RespondAsync("❌ This must be run inside a manual verification ticket channel.", ephemeral: true);
            return;
        }

        var topic = channel.Topic ?? "";
        if (!topic.Contains("type:manual_verify", StringComparison.OrdinalIgnoreCase))
        {
            await command.RespondAsync("❌ This channel is not a manual verification ticket (`type:manual_verify` missing in topic).", ephemeral: true);
            return;
        }

        if (!TryParseUlongToken(topic, "owner", out var ownerId))
        {
            await command.RespondAsync("❌ Could not find `owner:<id>` in the channel topic.", ephemeral: true);
            return;
        }

        // statusmsg:<messageId> stored by ManualVerifyOnJoinHandler (optional but recommended)
        var hasStatusMsg = TryParseUlongToken(topic, "statusmsg", out var statusMsgId);

        var guild = invoker.Guild;

        // ✅ Cached user (avoids IGuild.GetUserAsync explicit interface issues)
        var target = guild.GetUser(ownerId);

        // Apply roles if user is present in cache
        if (target != null)
        {
            var verifiedRole = guild.GetRole(VerifiedRoleId);
            if (verifiedRole == null)
            {
                await command.RespondAsync("❌ Verified role not found.", ephemeral: true);
                return;
            }

            await target.AddRoleAsync(verifiedRole);

            var manualRole = guild.GetRole(ManualVerifyRoleId);
            if (manualRole != null)
                await target.RemoveRoleAsync(manualRole);
        }

        // ✅ Update the "Verification Pending" embed -> "Verified"
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
                            target != null
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
                // If message was deleted / missing perms / etc., we still close the ticket
            }
        }

        // Close ticket: rename + move category
        var newName = channel.Name.StartsWith("closed-", StringComparison.OrdinalIgnoreCase)
            ? channel.Name
            : $"closed-{channel.Name}";

        await channel.ModifyAsync(props =>
        {
            props.CategoryId = ClosedManualVerifyCategoryId;
            props.Name = newName;
        });

        await command.RespondAsync(
            target != null
                ? $"✅ Verified {target.Mention}, updated status, and closed the ticket."
                : "✅ Updated status and closed the ticket. (Ticket owner not found in cache; roles may not have been applied.)",
            ephemeral: true);

        await channel.SendMessageAsync($"✅ Ticket closed by {command.User.Mention}.");
    }

    private static bool TryParseUlongToken(string topic, string key, out ulong value)
    {
        value = 0;

        var parts = topic.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var token = parts.FirstOrDefault(p => p.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase));
        if (token == null) return false;

        var str = token[(key.Length + 1)..].Trim(); // key:
        return ulong.TryParse(str, out value);
    }
}