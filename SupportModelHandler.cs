using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class SupportModalHandler
{
    private readonly ulong _supportCategoryId = 1393610364511326259;

    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355,
        1393623589122736238
    };

    public async Task HandleModalAsync(SocketModal modal)
    {
        if (!modal.Data.CustomId.StartsWith("ticket_reason:")) return;

        var ticketType = modal.Data.CustomId.Split(":")[1]; 
        var reason = modal.Data.Components.First(x => x.CustomId == "ticket_reason_input").Value;

        var user = modal.User as SocketGuildUser;
        var guild = user.Guild;
        
        string typePrefix = ticketType switch
        {
            "general" => "general",
            "game" => "game",
            "ban" => "disputes",
            "sub" => "sub",
            _ => "ticket"
        };

        string cleanName = user.Username.ToLower().Replace(" ", "").Replace("#", "").Replace(".", "");
        int rand = new Random().Next(100, 999);
        string channelName = $"{typePrefix}-{cleanName}-{rand}";
        
        var overwrites = new List<Overwrite>();

        overwrites.Add(new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
            new OverwritePermissions(viewChannel: PermValue.Deny)));

        overwrites.Add(new Overwrite(user.Id, PermissionTarget.User,
            new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));

        if (ticketType == "ban")
        {
            var headModRole = guild.GetRole(1393728468608487594);
            if (headModRole != null)
            {
                overwrites.Add(new Overwrite(headModRole.Id, PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
            }
        }
        else
        {
            foreach (var roleId in _moderatorRoleIds)
            {
                overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
            }
        }

        ulong categoryId = ticketType switch
        {
            "ban" => 1393628885484044299,
            _ => _supportCategoryId
        };

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = categoryId;
            props.PermissionOverwrites = overwrites;
        });
        
        var fullTypeLabel = ticketType switch
        {
            "general" => "General Support",
            "game" => "Game Support",
            "ban" => "Ban Appeals",
            "sub" => "Subscription Support",
            _ => "Support"
        };
        
        await channel.SendMessageAsync($"Thank you for creating a **{fullTypeLabel}** ticket!\n\n👋 {user.Mention}");
        var embed = new EmbedBuilder()
            .WithTitle($"📄 {fullTypeLabel} Ticket Information")
            .WithColor(Color.Orange)
            .WithDescription(
                $"A member of staff will be with you shortly.\n\n" +
                $"Below you will find the information you provided regarding the support request.\n" +
                $"If you think of anything else you would like to add to the support ticket, feel free to comment below.\n\n" +
                $"🔒 *Please note: A copy of the chat logs will be stored for audit, quality, and training purposes.*\n" +
                $"🔐 *Disclaimer: This ticket and its contents are confidential and should not be shared or discussed outside of this channel.*"
            )
            .AddField("📝 How can we help?", string.IsNullOrWhiteSpace(reason) ? "*No description provided.*" : reason.Trim(), false)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter(footer =>
            {
                footer.Text = $"Created by {user.Username}";
                footer.IconUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            })
            .Build();
        
        var buttons = new ComponentBuilder()
            .WithButton("🎯 Claim Ticket", customId: "ticket_claim", ButtonStyle.Primary)
            .WithButton("🔓 Release Ticket", customId: "ticket_release", ButtonStyle.Secondary);
        
        await channel.SendMessageAsync(embed: embed, components: buttons.Build());

        await modal.RespondAsync($"✅ Your ticket has been created: {channel.Mention}", ephemeral: true);
    }
}
