using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketRestrictCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    public TicketRestrictCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "ticketrestrict";
	public string Description => "Restrict a Ticket to a Role";

    private readonly ulong[] _staffRoleIds = { 1393623589122736238 }; // Discord Moderator role ID

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        var channel = command.Channel as SocketTextChannel;
        var role = command.Data.Options.FirstOrDefault(o => o.Name == "role")?.Value as SocketRole;

        if (channel == null || !channel.Name.StartsWith("ticket-"))
        {
            await command.RespondAsync("❌ This command can only be used in a ticket channel.", ephemeral: true);
            return;
        }

        if (role == null)
        {
            await command.RespondAsync("❌ You must specify a role.", ephemeral: true);
            return;
        }

        if (!_staffRoleIds.Any(roleId => user.Roles.Any(r => r.Id == roleId)))
        {
            await command.RespondAsync("❌ You do not have permission to restrict this ticket.", ephemeral: true);
            return;
        }

        foreach (var overwrite in channel.PermissionOverwrites)
        {
            if (overwrite.TargetType == PermissionTarget.Role && overwrite.TargetId != role.Id)
            {
                var existingRole = channel.Guild.GetRole(overwrite.TargetId);
                if (existingRole != null)
                    await channel.RemovePermissionOverwriteAsync(existingRole);
            }
        }

        await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        await command.RespondAsync($"Ticket now restricted to {role.Mention}.", ephemeral: true);
    }
}
