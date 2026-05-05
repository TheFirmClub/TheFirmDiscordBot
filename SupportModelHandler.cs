using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class SupportModalHandler
{
    private readonly IConfiguration _config;
    private readonly AiSupportService _aiSupport;

    private readonly ulong _supportCategoryId = 1393610364511326259;

    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355,
        1393623589122736238,
        1393590761953558608,
        1393638449709584434,
        1393728468608487594, // HEAD MOD
        1405330877440983130  // ASST HEAD MOD
        
    };
    
    public SupportModalHandler(IConfiguration config, AiSupportService aiSupport)
    {
        _config = config;
        _aiSupport = aiSupport;
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
        // 🔥 THIS LINE FIXES YOUR ERROR
        // ❌ Ignore ALL non-support modals
        if (!modal.Data.CustomId.StartsWith("ticket_reason:") 
            && modal.Data.CustomId != "ai_support_modal")
            return;

        // ✅ AI SUPPORT MODAL
        if (modal.Data.CustomId == "ai_support_modal")
        {
            await HandleAiSupportModal(modal);
            return;
        }

        await modal.DeferAsync(ephemeral: true);
        
        // ✅ NORMAL TICKET CREATION
        if (!modal.Data.CustomId.StartsWith("ticket_reason:"))
        {
            await modal.FollowupAsync("⚠️ Invalid modal.", ephemeral: true);
            return;
        }

        var ticketType = modal.Data.CustomId.Split(":")[1];
        var values = modal.Data.Components.ToDictionary(x => x.CustomId, x => (x.Value ?? string.Empty).Trim());
        values.TryGetValue("ticket_reason_input", out var reason);

        string charName = null;
        string evidence = null;

        if (ticketType == "reportplayer")
        {
            values.TryGetValue("staff_report_charname", out charName);
            values.TryGetValue("staff_report_evidence", out evidence);
        }

        var user = modal.User as SocketGuildUser;
        var guild = user.Guild;

        string typePrefix = ticketType switch
        {
            "general" => "general",
            "game" => "game",
            "reportplayer" => "report",
            "sub" => "sub",
            "reportstaff" => "staff",
            _ => "ticket"
        };

        int rand = new Random().Next(1000, 9999);
        string channelName = $"{typePrefix}-{rand}";

        var overwrites = new List<Overwrite>
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(user.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
        };

        if (ticketType == "reportstaff")
        {
            var headModRole = guild.GetRole(1393728468608487594);
            var asstHeadModRole = guild.GetRole(1405330877440983130);

            if (headModRole != null)
                overwrites.Add(new Overwrite(headModRole.Id, PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
            if (asstHeadModRole != null)
                overwrites.Add(new Overwrite(asstHeadModRole.Id, PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }
        else if (ticketType == "reportplayer")
        {
            overwrites.Add(new Overwrite(1393729574537396355, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }
        else
        {
            foreach (var roleId in _moderatorRoleIds)
                overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }

        ulong categoryId = ticketType switch
        {
            "ban" => 1393628885484044299,
            "reportstaff" => 1393627644326838292,
            _ => _supportCategoryId
        };

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = categoryId;
            props.PermissionOverwrites = overwrites;
            props.Topic = $"owner:{user.Id}; type:{ticketType}; created:{DateTimeOffset.UtcNow:O}";
        });
        
        var permissionService = new TicketPermissionService();

        var allowedRoles = overwrites
            .Where(o => o.TargetType == PermissionTarget.Role)
            .Where(o => o.TargetId != guild.EveryoneRole.Id)
            .Select(o => o.TargetId)
            .ToArray();

        await permissionService.SaveAsync(channel.Id, user.Id, allowedRoles);

        var fullTypeLabel = ticketType switch
        {
            "general" => "General Support",
            "game" => "Game Support",
            "reportplayer" => "Report a Player",
            "sub" => "Subscription Support",
            "reportstaff" => "Report a staff",
            _ => "Support"
        };

        if (ticketType == "reportstaff")
        {
            var headModRole = guild.GetRole(1393728468608487594);
            var asstHeadModRole = guild.GetRole(1405330877440983130);
            string mentions = "";
            if (headModRole != null) mentions += $"{headModRole.Mention} ";
            if (asstHeadModRole != null) mentions += $"{asstHeadModRole.Mention} ";
            if (!string.IsNullOrWhiteSpace(mentions))
                await channel.SendMessageAsync(mentions.Trim());
        }
        else if (ticketType == "reportplayer" || ticketType == "game")
        {
            var gameMod = guild.GetRole(1393729574537396355);
            if (gameMod != null)
                await channel.SendMessageAsync($"{gameMod.Mention}");
        }
        else
        {
            var discordMod = guild.GetRole(1393623589122736238);
            var gameMod = guild.GetRole(1393729574537396355);

            await channel.SendMessageAsync($"{discordMod?.Mention} {gameMod?.Mention}");
        }

        await channel.SendMessageAsync($"Thank you for creating a **{fullTypeLabel}** ticket!\n\n👋 {user.Mention}");

        var eb = new EmbedBuilder()
            .WithTitle($"📄 {fullTypeLabel} Ticket Information")
            .WithColor(Color.Orange)
            .WithDescription(
                "A member of staff will be with you shortly.\n\nBelow you will find the information you provided regarding the support request.\nIf you think of anything else you would like to add to the support ticket, feel free to comment below.\n\n🤖 If ticket is unclaimed by staff, feel free to click 'Ask AI Assistant' to attempt to resolve your game issues.\n\n🔒 *Please note: A copy of the chat logs will be stored for audit, quality, and training purposes.*\n🔐 *Disclaimer: This ticket and its contents are confidential and should not be shared or discussed outside of this channel.*")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter(footer =>
            {
                footer.Text = $"Created by {user.Username}";
                footer.IconUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            });

        if (ticketType == "reportplayer")
        {
            eb.AddField("Character Name of Player", string.IsNullOrWhiteSpace(charName) ? "*Not provided*" : charName,
                true);
            eb.AddField("Evidence", string.IsNullOrWhiteSpace(evidence) ? "*Not provided*" : evidence, true);
        }

        eb.AddField("📝 How can we help?", string.IsNullOrWhiteSpace(reason) ? "*No description provided.*" : reason,
            false);

        var buttons = new ComponentBuilder()
            .WithButton("🎯 Claim Ticket", customId: "ticket_claim", ButtonStyle.Primary)
            .WithButton("🔓 Release Ticket", customId: "ticket_release", ButtonStyle.Secondary);

        // ✅ ONLY add AI button for general + game
        if (ticketType == "general" || ticketType == "game")
        {
            buttons.WithButton(
                "🤖 Ask AI Assistant",
                customId: "ticket_ai",
                ButtonStyle.Success
            );
        }


        await channel.SendMessageAsync(embed: eb.Build(), components: buttons.Build());
        await modal.FollowupAsync($"✅ Your ticket has been created: {channel.Mention}", ephemeral: true);
    }
    
    private async Task HandleAiSupportModal(SocketModal modal)
    {
        try
        {
            await modal.DeferAsync(ephemeral: true);

            var issue = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "ai_issue")?.Value;

            var attempts = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "ai_attempts")?.Value;

            if (string.IsNullOrWhiteSpace(issue))
            {
                await modal.FollowupAsync(
                    "⚠️ Please describe your issue.",
                    ephemeral: true);
                return;
            }

            var channel = modal.Channel as SocketTextChannel;

            if (channel == null)
            {
                await modal.FollowupAsync(
                    "⚠️ AI unavailable in this channel.",
                    ephemeral: true);
                return;
            }

            await channel.SendMessageAsync(
                "🤖 Support Assistant is analyzing your issue...");

            var reply = await _aiSupport.GetSupportReplyAsync(
                issue,
                attempts,
                "FiveM Support"
            );

            await channel.SendMessageAsync(reply);
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 AI MODAL CRASH:");
            Console.WriteLine(ex.ToString());

            try
            {
                await modal.FollowupAsync(
                    "⚠️ Support Assistant crashed. Staff have been notified.",
                    ephemeral: true);
            }
            catch { }
        }
    }

}