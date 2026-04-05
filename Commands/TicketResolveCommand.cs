using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class TicketResolveCommand : ISlashCommand
{
    public string Name => "ticketresolve";
    public string Description => "Mark the ticket as resolved";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.RespondAsync("❌ This command can only be used in a ticket channel.", ephemeral: true);
            return;
        }

        await command.DeferAsync(ephemeral: true);

        // 🔎 Get ticket owner (from topic, or fallback from overwrites)
        var owner = GetTicketOwner(channel) ?? GetOwnerFromOverwrites(channel);

        string ownerMention = owner != null ? owner.Mention : string.Empty;

        var confirmEmbed = new EmbedBuilder()
            .WithTitle("📩 Resolution Confirmation")
            .WithDescription(
                $"{(owner != null ? ownerMention : "Ticket Owner")}, a staff member has requested to resolve this ticket.\n\n" +
                "Do you have anything else to add, or is your enquiry resolved?\n\n" +
                "Please let us know by clicking one of the buttons below.")
            .WithColor(Color.Orange)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        var confirmButtons = new ComponentBuilder()
            .WithButton("✅ Resolved", "ticket_confirm_resolved", ButtonStyle.Success)
            .WithButton("❌ Not Resolved", "ticket_confirm_unresolved", ButtonStyle.Danger);

        await channel.SendMessageAsync(
            ownerMention,                      // message content (string)
            embed: confirmEmbed,               // the embed
            components: confirmButtons.Build() // the buttons
        );

        await command.FollowupAsync("✅ Ticket resolve confirmation sent.", ephemeral: true);

        var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
        if (logChannel != null)
        {
            var logEmbed = new EmbedBuilder()
                .WithTitle("📌 Ticket Resolved (Prompted)")
                .AddField("Resolved By", user.Mention, true)
                .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", true);

            if (owner != null)
                logEmbed.AddField("Owner", owner.Mention, true);

            logEmbed.WithColor(Color.Orange)
                .WithTimestamp(DateTimeOffset.UtcNow);

            await logChannel.SendMessageAsync(embed: logEmbed.Build());
        }
    }

    // --- Helpers ---

    private SocketGuildUser? GetTicketOwner(SocketTextChannel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Topic)) return null;

        var parts = channel.Topic.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
            {
                var idText = part.Substring("owner:".Length).Trim();
                if (ulong.TryParse(idText, out var ownerId))
                    return channel.Guild.GetUser(ownerId);
            }
        }
        return null;
    }

    private SocketGuildUser? GetOwnerFromOverwrites(SocketTextChannel channel)
    {
        var guild = channel.Guild;
        var moderatorRoleIds = new ulong[]
        {
            1393729574537396355, // Game Mod
            1393623589122736238, // Discord Mod
            1393590761953558608, // Senior Management
            1393638449709584434, // Senior Mod
            1393728468608487594, // Head Mod
            1405330877440983130, // Assistant Head Mod
        };

        foreach (var ow in channel.PermissionOverwrites)
        {
            if (ow.TargetType != PermissionTarget.User) continue;

            var perms = ow.Permissions;
            if (perms.ViewChannel == PermValue.Allow && perms.SendMessages == PermValue.Allow)
            {
                var candidate = guild.GetUser(ow.TargetId);
                if (candidate != null && !candidate.IsBot &&
                    !candidate.Roles.Any(r => moderatorRoleIds.Contains(r.Id)))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 ||
                r.Id == 1393623589122736238 ||
                r.Id == 1393638449709584434 ||
                r.Id == 1420513009729802260 ||
                r.Id == 1393590761953558608);
        }

        public static bool IsSeniorModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393638449709584434 ||
                r.Id == 1393590761953558608);
        }
    }
}
