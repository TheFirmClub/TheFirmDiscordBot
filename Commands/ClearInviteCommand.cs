using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class ClearInvitesCommand : ISlashCommand
{
    public string Name => "clearinvites";
    public string Description => "Clear all tracked invite statistics for this server.";

    private const ulong SeniorManagementRoleId = 1393590761953558608;

    private readonly InviteTrackerService _tracker;
    public ClearInvitesCommand(InviteTrackerService tracker) => _tracker = tracker;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.GuildId is null)
        {
            await command.RespondAsync("Use this in a server.", ephemeral: true);
            return;
        }

        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("Unable to resolve user.", ephemeral: true);
            return;
        }

        // Role check
        if (!user.Roles.Any(r => r.Id == SeniorManagementRoleId))
        {
            await command.RespondAsync(
                "You do not have permission to use this command.",
                ephemeral: true
            );
            return;
        }

        _tracker.ClearGuildStats(command.GuildId.Value);

        await command.RespondAsync(
            "✅ Invite statistics have been cleared for this server.",
            ephemeral: true
        );
    }
}
