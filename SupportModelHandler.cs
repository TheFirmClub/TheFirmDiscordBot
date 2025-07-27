using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class SupportModalHandler
{
    private readonly ulong _supportCategoryId = 1393610364511326259; // 🔁 Replace with your category ID

    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355,
        1393623589122736238
    };

    public async Task HandleModalAsync(SocketModal modal)
    {
        if (!modal.Data.CustomId.StartsWith("ticket_reason:")) return;

        var ticketType = modal.Data.CustomId.Split(":")[1]; // e.g., "general", "ban"
        var reason = modal.Data.Components.First(x => x.CustomId == "ticket_reason_input").Value;

        var user = modal.User as SocketGuildUser;
        var guild = user.Guild;

        // Format the channel name
        string typePrefix = ticketType switch
        {
            "general" => "general",
            "game" => "game",
            "ban" => "banappeal",
            "sub" => "sub",
            _ => "ticket"
        };

        string cleanName = user.Username.ToLower().Replace(" ", "").Replace("#", "").Replace(".", "");
        int rand = new Random().Next(100, 999);
        string channelName = $"{typePrefix}-{cleanName}-{rand}";

        // Build permissions
        var overwrites = new List<Overwrite>
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),

            new Overwrite(user.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
        };

        foreach (var roleId in _moderatorRoleIds)
        {
            overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }

        // Create channel
        ulong categoryId = ticketType switch
        {
            "ban" => 1393628885484044299, // ⬅️ New ban ticket category
            _ => _supportCategoryId
        };

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = categoryId;
            props.PermissionOverwrites = overwrites;
        });

        // Post ticket content
        await channel.SendMessageAsync($"🎫 **New support ticket by {user.Mention}**\n**Type:** `{typePrefix}`\n**Reason:** {reason}");

        // Respond to modal submit
        await modal.RespondAsync($"✅ Your ticket has been created: {channel.Mention}", ephemeral: true);
    }
}
