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
        
        var embed = new EmbedBuilder()
            .WithTitle("🎫 Support Ticket")
            .WithDescription($"**User:** {user.Mention}\n**Type:** `{typePrefix}`\n**Reason:**\n```{reason}```")
            .WithColor(Color.Orange)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        var buttons = new ComponentBuilder()
            .WithButton("🎯 Claim Ticket", customId: "ticket_claim", ButtonStyle.Primary)
            .WithButton("🔓 Release Ticket", customId: "ticket_release", ButtonStyle.Secondary);

        await channel.SendMessageAsync(embed: embed, components: buttons.Build());
        await modal.RespondAsync($"✅ Your ticket has been created: {channel.Mention}", ephemeral: true);
    }
}
