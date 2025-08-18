using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System;

public class TicketButtonHandler
{
    private readonly IConfiguration _config;

    public TicketButtonHandler(IConfiguration config)
    {
        _config = config;
    }

    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355, // Game Mod
        1393623589122736238, // Discord Mod
        1393590761953558608 // Senior Management
    };

    private readonly ulong[] _keepRoleIds = new ulong[]
    {
        1393638449709584434, // Senior Moderator
        1393590761953558608 // Senior Management
    };

    public async Task HandleAsync(SocketMessageComponent component)
    {
        if (!component.Data.CustomId.StartsWith("ticket_"))
            return;

        var user = component.User as SocketGuildUser;
        var originalMessage = component.Message;

        var originalEmbed = originalMessage.Embeds.FirstOrDefault();
        var embedBuilder = new EmbedBuilder();
        if (originalEmbed != null)
        {
            embedBuilder.WithTitle(originalEmbed.Title)
                .WithDescription(originalEmbed.Description)
                .WithColor(originalEmbed.Color.GetValueOrDefault(Color.Orange))
                .WithTimestamp(originalEmbed.Timestamp ?? DateTimeOffset.UtcNow)
                .WithFooter(originalEmbed.Footer?.Text, originalEmbed.Footer?.IconUrl);

            foreach (var field in originalEmbed.Fields)
                embedBuilder.AddField(field.Name, field.Value, field.Inline);
        }

        bool isMod = IsModerator(user);

        ulong? ticketOwnerId = null;
        if (component.Channel is SocketTextChannel chForOwner)
            ticketOwnerId = await GetTicketOwnerIdAsync(chForOwner, originalMessage);

        bool isOwner = ticketOwnerId.HasValue && user.Id == ticketOwnerId.Value;

        switch (component.Data.CustomId)
        {
            // ===== Moderators only =====
            case "ticket_claim":
            {
                if (!isMod)
                {
                    await component.RespondAsync("❌ Only moderators can use this.", ephemeral: true);
                    return;
                }

                if (originalMessage.Components.First().Components.FirstOrDefault(b => b.CustomId == "ticket_claim") is
                        ButtonComponent claimBtn && claimBtn.IsDisabled)
                {
                    await component.RespondAsync("⚠️ This ticket has already been claimed.", ephemeral: true);
                    return;
                }

                embedBuilder.AddField("👮 Claimed By", user.Mention, true);

                var claimButtons = new ComponentBuilder()
                    .WithButton("🎯 Claimed", "ticket_claim", ButtonStyle.Success, disabled: true)
                    .WithButton("🔓 Release Ticket", "ticket_release", ButtonStyle.Secondary);

                await originalMessage.ModifyAsync(m =>
                {
                    m.Embed = embedBuilder.Build();
                    m.Components = claimButtons.Build();
                });

                await component.RespondAsync($"🎯 Ticket claimed by {user.Mention}.", ephemeral: false);
                break;
            }

            case "ticket_release":
            {
                if (!isMod)
                {
                    await component.RespondAsync("❌ Only moderators can use this.", ephemeral: true);
                    return;
                }

                var updatedEmbed = new EmbedBuilder();
                updatedEmbed.WithTitle(embedBuilder.Title)
                    .WithDescription(embedBuilder.Description)
                    .WithColor(embedBuilder.Color.GetValueOrDefault(Color.Orange))
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .WithFooter(embedBuilder.Footer?.Text, embedBuilder.Footer?.IconUrl);

                foreach (var field in embedBuilder.Fields.Where(f => f.Name != "👮 Claimed By"))
                    updatedEmbed.AddField(field.Name, field.Value, field.IsInline);

                var resetButtons = new ComponentBuilder()
                    .WithButton("🎯 Claim Ticket", "ticket_claim", ButtonStyle.Primary)
                    .WithButton("🔓 Release Ticket", "ticket_release", ButtonStyle.Secondary);

                await originalMessage.ModifyAsync(m =>
                {
                    m.Embed = updatedEmbed.Build();
                    m.Components = resetButtons.Build();
                });

                await component.RespondAsync($"🔓 Ticket released by {user.Mention}.", ephemeral: false);
                break;
            }

            case "ticket_close":
            {
                if (!TicketCloseCommand.PermissionHelper.IsSeniorModerator(user))
                {
                    await component.RespondAsync("❌ Only Senior Moderators can close tickets.", ephemeral: true);
                    return;
                }

                if (component.Channel is SocketTextChannel channel)
                {
                    await component.RespondAsync("⏳ Closing ticket...", ephemeral: true);

                    var closeCommand = new TicketCloseCommand(_config);
                    await closeCommand.CloseTicketAsync(channel, user);
                }

                break;
            }

            case "ticket_confirm_resolved":
            {
                if (!(isMod || isOwner))
                {
                    await component.RespondAsync("❌ Only the ticket owner or a moderator can mark this as resolved.",
                        ephemeral: true);
                    return;
                }

                await component.DeferAsync(ephemeral: true);

                if (component.Channel is SocketTextChannel channel)
                {
                    var keepRoleIds = new HashSet<ulong>(_keepRoleIds); // Senior Mod + Senior Management
                    var keepUserIds = new HashSet<ulong>
                    {
                        channel.Guild.CurrentUser.Id // keep the bot
                    };

                    // Remove all *role & user* overwrites except the keep lists
                    foreach (var overwrite in channel.PermissionOverwrites.ToArray())
                    {
                        if (overwrite.TargetType == PermissionTarget.Role)
                        {
                            if (!keepRoleIds.Contains(overwrite.TargetId))
                            {
                                var role = channel.Guild.GetRole(overwrite.TargetId);
                                if (role != null) await channel.RemovePermissionOverwriteAsync(role);
                            }
                        }
                        else if (overwrite.TargetType == PermissionTarget.User)
                        {
                            if (!keepUserIds.Contains(overwrite.TargetId))
                            {
                                var member = channel.Guild.GetUser(overwrite.TargetId);
                                if (member != null) await channel.RemovePermissionOverwriteAsync(member);
                            }
                        }
                    }

                    // 🚫 Deny @everyone
                    await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
                        new OverwritePermissions(viewChannel: PermValue.Deny));

                    // 🚫 Explicitly remove the ticket creator’s access (only if they’re not in kept roles)
                    if (ticketOwnerId.HasValue)
                    {
                        var ownerUser = channel.Guild.GetUser(ticketOwnerId.Value);
                        if (ownerUser != null)
                        {
                            bool ownerHasKeptRole = ownerUser.Roles.Any(r => keepRoleIds.Contains(r.Id));
                            if (!ownerHasKeptRole)
                            {
                                await channel.AddPermissionOverwriteAsync(ownerUser,
                                    new OverwritePermissions(viewChannel: PermValue.Deny));
                            }
                        }
                    }

                    // 🏷 Move to resolved category
                    await channel.ModifyAsync(props => props.CategoryId = 1393610408706965656);

                    // 🧾 Resolved embed + close button
                    var embed = new EmbedBuilder()
                        .WithTitle("✅ Ticket Resolved")
                        .WithDescription(
                            $"This ticket has been marked as **resolved** by {component.User.Mention}.\n\n" +
                            "Kindly review the context before closing.\n\n" +
                            "Once reviewed, click the **Close Ticket** button below.")
                        .WithColor(Color.Red)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    var button = new ComponentBuilder()
                        .WithButton("🚫 Close Ticket", "ticket_close", ButtonStyle.Danger);

                    await channel.SendMessageAsync(embed: embed, components: button.Build());
                }

                break;
            }


            case "ticket_confirm_unresolved":
            {
                if (!(isMod || isOwner))
                {
                    await component.RespondAsync("❌ Only the ticket owner or a moderator can do that.",
                        ephemeral: true);
                    return;
                }

                await component.DeferAsync(ephemeral: true);

                var original = component.Message.Embeds.FirstOrDefault();
                var embed = new EmbedBuilder();
                if (original != null)
                {
                    embed.WithTitle(original.Title)
                        .WithDescription(original.Description)
                        .WithColor(original.Color.GetValueOrDefault(Color.Orange))
                        .WithTimestamp(original.Timestamp ?? DateTimeOffset.UtcNow);
                }

                var disabledButtons = new ComponentBuilder()
                    .WithButton("✅ Resolved", "ticket_confirm_resolved", ButtonStyle.Success, disabled: false)
                    .WithButton("❌ Not Resolved", "ticket_confirm_unresolved", ButtonStyle.Danger, disabled: true);

                await component.Message.ModifyAsync(msg =>
                {
                    msg.Embed = embed.Build();
                    msg.Components = disabledButtons.Build();
                });

                await component.FollowupAsync(
                    "🔁 Got it. A member of staff will follow up shortly. Is there anything else we can help with?",
                    ephemeral: false);
                break;
            }
        }
    }

    private bool IsModerator(SocketGuildUser user)
        => user != null && user.Roles.Any(r => _moderatorRoleIds.Contains(r.Id));

    private async Task<ulong?> GetTicketOwnerIdAsync(SocketTextChannel channel, IUserMessage originalMessage)
    {
        // 1) From channel topic
        if (!string.IsNullOrWhiteSpace(channel.Topic))
        {
            var m = Regex.Match(channel.Topic, @"owner:(\d{15,20})");
            if (m.Success && ulong.TryParse(m.Groups[1].Value, out var idFromTopic))
                return idFromTopic;
        }

        // 2) From original embed: first user mention pattern <@123> or <@!123>
        var embed = originalMessage?.Embeds?.FirstOrDefault();
        if (embed != null)
        {
            var maybe = ExtractFirstMentionedUserId(embed.Description);
            if (maybe.HasValue) return maybe.Value;

            foreach (var f in embed.Fields)
            {
                maybe = ExtractFirstMentionedUserId(f.Value);
                if (maybe.HasValue) return maybe.Value;
            }
        }

        await Task.CompletedTask;
        return null;
    }

    private ulong? ExtractFirstMentionedUserId(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // Matches <@123...> or <@!123...>
        var m = Regex.Match(text, @"<@!?(\d{15,20})>");
        if (m.Success && ulong.TryParse(m.Groups[1].Value, out var uid))
            return uid;

        return null;
    }
}