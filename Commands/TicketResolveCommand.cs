using Discord;
using Discord.WebSocket;
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

        await command.DeferAsync(ephemeral: true); // ✅ Avoid timeout errors

        // 🔐 Remove all current overwrites
        foreach (var overwrite in channel.PermissionOverwrites)
        {
            if (overwrite.TargetType == PermissionTarget.User)
            {
                var u = channel.Guild.GetUser(overwrite.TargetId);
                if (u != null)
                    await channel.RemovePermissionOverwriteAsync(u);
            }
            else if (overwrite.TargetType == PermissionTarget.Role)
            {
                var r = channel.Guild.GetRole(overwrite.TargetId);
                if (r != null)
                    await channel.RemovePermissionOverwriteAsync(r);
            }
        }

        // 🚫 Deny @everyone
        await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
            new OverwritePermissions(viewChannel: PermValue.Deny));

        // ✅ Allow senior moderators
        ulong seniorModRoleId = 1393638449709584434;
        var seniorRole = channel.Guild.GetRole(seniorModRoleId);
        if (seniorRole != null)
        {
            await channel.AddPermissionOverwriteAsync(seniorRole,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        }
        
        await channel.ModifyAsync(props => props.CategoryId = 1393610408706965656);

        // 🧾 Build embed
        var embed = new EmbedBuilder()
            .WithTitle("✅ Ticket Resolved")
            .WithDescription($"This ticket has been marked as **resolved** by {user.Mention}.\n\n" +
                             "Kindly review the context before closing.\n\n" +
                             "Once reviewed, click the **Close Ticket** button below.")
            .WithColor(Color.Red)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        // 🔴 Close button
        var button = new ComponentBuilder()
            .WithButton("🚫 Close Ticket", "ticket_close", ButtonStyle.Danger);

        // Post embed + button
        await channel.SendMessageAsync(embed: embed, components: button.Build());

        // Respond to slash command
        await command.FollowupAsync("✅ Ticket resolved. Senior moderators may now review and close it.", ephemeral: true);
        
        // Log to ticket log channel
        var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
        if (logChannel != null)
        {
            var logEmbed = new EmbedBuilder()
                .WithTitle("📌 Ticket Resolved")
                .AddField("Resolved By", user.Mention, true)
                .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", true)
                .WithColor(Color.Orange)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: logEmbed);
        }
    }

    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 || r.Id == 1393623589122736238 ||
                r.Id == 1393638449709584434 || r.Id == 1393590761953558608); 
        }

        public static bool IsSeniorModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r => r.Id == 1393638449709584434);
        }
    }
}
