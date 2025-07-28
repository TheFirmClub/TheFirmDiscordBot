using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketRestrictCommand : ISlashCommand
{
    public string Name => "ticketrestrict";
    public string Description => "Restrict this ticket to a specific role";

    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355,
        1393623589122736238
    };

    private readonly ulong _seniorModRoleId = 1393638449709584434;
    private readonly ulong _restrictedCategoryId = 1393627644326838292;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!TicketAddRoleCommand.PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.RespondAsync("❌ This command must be used in a ticket channel.", ephemeral: true);
            return;
        }

        if (command.Data.Options == null || !command.Data.Options.Any())
        {
            await command.RespondAsync("❌ You must mention a role to restrict to.", ephemeral: true);
            return;
        }

        await command.DeferAsync(ephemeral: true);

        var targetRole = (SocketRole)command.Data.Options.First().Value;

        await channel.ModifyAsync(props => props.CategoryId = _restrictedCategoryId);

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

        await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
            new OverwritePermissions(viewChannel: PermValue.Deny));

        foreach (var modRoleId in _moderatorRoleIds)
        {
            var role = channel.Guild.GetRole(modRoleId);
            if (role != null)
                await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(viewChannel: PermValue.Deny));
        }

        await channel.AddPermissionOverwriteAsync(targetRole,
            new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));

        var senior = channel.Guild.GetRole(_seniorModRoleId);
        if (senior != null)
        {
            await channel.AddPermissionOverwriteAsync(senior,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        }

        await command.FollowupAsync($"🔒 Ticket has been restricted to {targetRole.Mention} and senior moderators.",
            ephemeral: true);

        var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
        if (logChannel != null)
        {
            var logEmbed = new EmbedBuilder()
                .WithTitle("🔐 Ticket Restricted")
                .AddField("Restricted By", user.Mention, true)
                .AddField("Restricted To", targetRole.Mention, true)
                .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", false)
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: logEmbed);
        }
    }
}