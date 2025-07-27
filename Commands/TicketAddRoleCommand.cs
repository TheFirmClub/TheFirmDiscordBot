using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class TicketAddRoleCommand : ISlashCommand
{
    public string Name => "ticketaddrole";

    private readonly TicketService _ticketService;

    public TicketAddRoleCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var channel = command.Channel as SocketTextChannel;
        if (channel == null || !channel.Name.StartsWith("ticket-"))
        {
            await command.RespondAsync("❌ This command can only be used in a ticket channel.", ephemeral: true);
            return;
        }

        var roleOption = command.Data.Options.FirstOrDefault();
        if (roleOption?.Value is not SocketRole role)
        {
            await command.RespondAsync("❌ Please provide a valid role.", ephemeral: true);
            return;
        }

        await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(
            viewChannel: PermValue.Allow,
            sendMessages: PermValue.Allow,
            readMessageHistory: PermValue.Allow
        ));

        await command.RespondAsync($"✅ {role.Mention} has been granted access to this ticket.");
    }
}
