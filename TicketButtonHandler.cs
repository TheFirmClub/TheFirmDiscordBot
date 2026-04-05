using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;

public class TicketButtonHandler
{
    private readonly IConfiguration _config;
    private readonly AiSupportService _aiSupport;

    // ✅ AI cooldown
    private static readonly Dictionary<ulong, DateTime> _aiCooldown = new();

    public TicketButtonHandler(IConfiguration config, AiSupportService aiSupport)
    {
        _config = config;
        _aiSupport = aiSupport;
    }

    private readonly ulong[] _moderatorRoleIds =
    {
        1393729574537396355,
        1393623589122736238,
        1393590761953558608
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

        var isMod = IsModerator(user);

        ulong? ticketOwnerId = null;
        if (component.Channel is SocketTextChannel chForOwner)
            ticketOwnerId = await GetTicketOwnerIdAsync(chForOwner, originalMessage);

        var isOwner = ticketOwnerId.HasValue && user.Id == ticketOwnerId.Value;

        switch (component.Data.CustomId)
        {
            case "ticket_ai":
            {
                // ✅ Cooldown check
                if (_aiCooldown.TryGetValue(component.User.Id, out var lastUsed))
                    if ((DateTime.UtcNow - lastUsed).TotalMinutes < 3)
                    {
                        await component.RespondAsync(
                            "⏳ Please wait a few minutes before requesting AI help again.",
                            ephemeral: true);
                        return;
                    }

                var channel = component.Channel as SocketTextChannel;

                if (channel == null)
                {
                    await component.RespondAsync(
                        "⚠️ AI unavailable in this channel.",
                        ephemeral: true);
                    return;
                }

                // ✅ BACKEND ticket type protection (VERY important)
                if (channel.Topic == null ||
                    (!channel.Topic.Contains("type:general") &&
                     !channel.Topic.Contains("type:game")))
                {
                    await component.RespondAsync(
                        "⚠️ The Support Assistant is not available for this ticket type.",
                        ephemeral: true);
                    return;
                }

                // 🚫 Disable if claimed
                var claimedField = embedBuilder.Fields
                    .FirstOrDefault(f => f.Name.Contains("Claimed"));

                if (claimedField != null)
                {
                    await component.RespondAsync(
                        "👮 A moderator is already assisting this ticket.",
                        ephemeral: true);
                    return;
                }

                // ✅ Set cooldown BEFORE modal opens
                _aiCooldown[component.User.Id] = DateTime.UtcNow;

                // ⭐ BUILD MODAL
                var modal = new ModalBuilder()
                    .WithTitle("FiveM Support Assistant")
                    .WithCustomId("ai_support_modal")
                    .AddTextInput(
                        "Describe your issue",
                        "ai_issue",
                        TextInputStyle.Paragraph,
                        "Explain the problem you're having...",
                        required: true,
                        maxLength: 1000)
                    .AddTextInput(
                        "What troubleshooting have you tried?",
                        "ai_attempts",
                        TextInputStyle.Paragraph,
                        "Cache cleared? Restarted FiveM?",
                        required: false,
                        maxLength: 500);

                await component.RespondWithModalAsync(modal.Build());

                return;
            }

            case "ticket_claim":
            {
                if (!isMod)
                {
                    await component.RespondAsync("❌ Only moderators can use this.", ephemeral: true);
                    return;
                }

                embedBuilder.AddField("👮 Claimed By", user.Mention, true);

                var claimButtons = new ComponentBuilder()
                    .WithButton("🎯 Claimed", "ticket_claim", ButtonStyle.Success, disabled: true)
                    .WithButton("🔓 Release Ticket", "ticket_release", ButtonStyle.Secondary)
                    .WithButton("🤖 AI Disabled", "ticket_ai_disabled", ButtonStyle.Secondary, disabled: true);

                await originalMessage.ModifyAsync(m =>
                {
                    m.Embed = embedBuilder.Build();
                    m.Components = claimButtons.Build();
                });

                ulong logChannelId = 1394405064520499415;

                var guild = (component.Channel as SocketGuildChannel)?.Guild;
                var logChannel = guild?.GetTextChannel(logChannelId);

                if (logChannel != null)
                {
                    var logEmbed = new EmbedBuilder()
                        .WithTitle("🔒 Ticket Claimed")
                        .AddField("Moderator", user.Mention, true)
                        .AddField("Ticket", $"{component.Channel.Name} (`{component.Channel.Id}`)", true)
                        .WithColor(Color.Blue)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await logChannel.SendMessageAsync(embed: logEmbed);
                }

                await component.RespondAsync($"🎯 Ticket claimed by {user.Mention}.");
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
                    .WithButton("🎯 Claim Ticket", "ticket_claim")
                    .WithButton("🔓 Release Ticket", "ticket_release", ButtonStyle.Secondary)
                    .WithButton("🤖 Ask Support Assistant", "ticket_ai", ButtonStyle.Success);

                await originalMessage.ModifyAsync(m =>
                {
                    m.Embed = updatedEmbed.Build();
                    m.Components = resetButtons.Build();
                });

                await component.RespondAsync($"🔓 Ticket released by {user.Mention}.");
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
                await component.DeferAsync(true);

                if (component.Channel is SocketTextChannel channel)
                {
                    var permissionService = new TicketPermissionService();

                    // ✅ FIX: convert to List
                    var allowedRoles = (await permissionService.GetRolesAsync(channel.Id)).ToList();

                    // role IDs
                    const ulong HEAD_MOD = 1393728468608487594;
                    const ulong ASST_HEAD = 1405330877440983130;

                    // get roles
                    var headModRole = channel.Guild.GetRole(HEAD_MOD);
                    var asstHeadRole = channel.Guild.GetRole(ASST_HEAD);

                    // pick highest leadership role
                    var compareRole = new[] { headModRole, asstHeadRole }
                        .Where(r => r != null)
                        .OrderByDescending(r => r.Position)
                        .FirstOrDefault();

                    // get highest current role
                    var highestRole = allowedRoles
                        .Select(id => channel.Guild.GetRole(id))
                        .Where(r => r != null)
                        .OrderByDescending(r => r.Position)
                        .FirstOrDefault();

                    // ✅ ONLY add leadership if below them
                    if (highestRole != null && compareRole != null)
                        if (highestRole.Position < compareRole.Position)
                        {
                            if (!allowedRoles.Contains(HEAD_MOD))
                                allowedRoles.Add(HEAD_MOD);

                            if (!allowedRoles.Contains(ASST_HEAD))
                                allowedRoles.Add(ASST_HEAD);
                        }

                    // ✅ cleanup duplicates
                    allowedRoles = allowedRoles.Distinct().ToList();

                    // ❌ Remove ALL overwrites
                    foreach (var overwrite in channel.PermissionOverwrites.ToArray())
                        if (overwrite.TargetType == PermissionTarget.Role)
                        {
                            var role = channel.Guild.GetRole(overwrite.TargetId);
                            if (role != null)
                                await channel.RemovePermissionOverwriteAsync(role);
                        }
                        else if (overwrite.TargetType == PermissionTarget.User)
                        {
                            var member = channel.Guild.GetUser(overwrite.TargetId);
                            if (member != null)
                                await channel.RemovePermissionOverwriteAsync(member);
                        }

                    // 🚫 Deny everyone
                    await channel.AddPermissionOverwriteAsync(
                        channel.Guild.EveryoneRole,
                        new OverwritePermissions(viewChannel: PermValue.Deny)
                    );

                    // ✅ Re-add ONLY roles
                    foreach (var roleId in allowedRoles)
                    {
                        var role = channel.Guild.GetRole(roleId);
                        if (role != null)
                            await channel.AddPermissionOverwriteAsync(role,
                                new OverwritePermissions(
                                    viewChannel: PermValue.Allow,
                                    sendMessages: PermValue.Allow
                                ));
                    }

                    // 📂 Move category
                    var closedCategoryId =
                        channel.Topic != null && channel.Topic.Contains("type:manual_verify")
                            ? 1474419760237379846UL
                            : 1393610408706965656UL;

                    await channel.ModifyAsync(props =>
                    {
                        props.CategoryId = closedCategoryId;

                        if (!channel.Name.StartsWith("closed-"))
                            props.Name = $"closed-{channel.Name}";
                    });

                    // 📩 Send message
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
                await component.DeferAsync(true);

                var original = component.Message.Embeds.FirstOrDefault();
                var embed = new EmbedBuilder();
                if (original != null)
                    embed.WithTitle(original.Title)
                        .WithDescription(original.Description)
                        .WithColor(original.Color.GetValueOrDefault(Color.Orange))
                        .WithTimestamp(original.Timestamp ?? DateTimeOffset.UtcNow);

                var disabledButtons = new ComponentBuilder()
                    .WithButton("✅ Resolved", "ticket_confirm_resolved", ButtonStyle.Success)
                    .WithButton("❌ Not Resolved", "ticket_confirm_unresolved", ButtonStyle.Danger, disabled: true);

                await component.Message.ModifyAsync(msg =>
                {
                    msg.Embed = embed.Build();
                    msg.Components = disabledButtons.Build();
                });

                await component.FollowupAsync(
                    "🔁 Got it. A member of staff will follow up shortly. Is there anything else we can help with?");
                break;
            }
        }
    }

    private bool IsModerator(SocketGuildUser user)
    {
        return user != null && user.Roles.Any(r => _moderatorRoleIds.Contains(r.Id));
    }

    private async Task<ulong?> GetTicketOwnerIdAsync(SocketTextChannel channel, IUserMessage originalMessage)
    {
        if (!string.IsNullOrWhiteSpace(channel.Topic))
        {
            var m = Regex.Match(channel.Topic, @"owner:(\d{15,20})");
            if (m.Success && ulong.TryParse(m.Groups[1].Value, out var idFromTopic))
                return idFromTopic;
        }

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

        var m = Regex.Match(text, @"<@!?(\d{15,20})>");
        if (m.Success && ulong.TryParse(m.Groups[1].Value, out var uid))
            return uid;

        return null;
    }
}