using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class TicketAddUserCommand : ISlashCommand
{
    public string Name => "ticketadduser";
	public string Description => "Add a user to the ticket";

    private readonly TicketService _ticketService;

    public TicketAddUserCommand(TicketService ticketService)
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

        var userOption = command.Data.Options.FirstOrDefault();
        if (userOption?.Value is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ Please mention a valid user.", ephemeral: true);
            return;
        }

        await channel.AddPermissionOverwriteAsync(user, new OverwritePermissions(
            viewChannel: PermValue.Allow,
            sendMessages: PermValue.Allow,
            readMessageHistory: PermValue.Allow
        ));

        await command.RespondAsync($"✅ {user.Mention} has been added to this ticket.");
    }
}
