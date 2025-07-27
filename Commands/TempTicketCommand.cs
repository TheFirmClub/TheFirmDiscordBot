using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class TempTicketCommand : ISlashCommand
{
    private readonly TicketService _ticketService;

    public TempTicketCommand(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    public string Name => "tempticket";
	public string Description => "Staff Member to create a Temp Ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var userOption = command.Data.Options.FirstOrDefault();

        if (userOption == null || userOption.Value is not SocketGuildUser mentionedUser)
        {
            await command.RespondAsync("❌ You must mention a valid user to open a temp ticket with.", ephemeral: true);
            return;
        }

        var channel = command.Channel as SocketTextChannel;
        if (channel == null)
        {
            await command.RespondAsync("❌ This command must be used in a server channel.", ephemeral: true);
            return;
        }

        var guild = channel.Guild;
        var staffUser = command.User as SocketGuildUser;

        string channelName = $"temp-{mentionedUser.Username.ToLower()}";
        var existing = guild.TextChannels.FirstOrDefault(c => c.Name == channelName);

        if (existing != null)
        {
            await command.RespondAsync($"⚠️ Temp ticket already exists: {existing.Mention}", ephemeral: true);
            return;
        }

        var overwrites = new Overwrite[]
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(mentionedUser.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)),
            new Overwrite(staffUser.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
        };

        var ticketChannel = await guild.CreateTextChannelAsync(channelName, c =>
        {
            c.CategoryId = null; // Optional: You can set a specific temp ticket category if desired
            c.PermissionOverwrites = overwrites;
            c.Topic = $"Temporary ticket between {staffUser.Username} and {mentionedUser.Username}";
        });

        var embed = new EmbedBuilder()
            .WithTitle("🔒 Temporary Ticket")
            .WithDescription($"Hello {mentionedUser.Mention}, a staff member has opened a temporary private ticket with you.\n\nFeel free to chat here. Use `/close` when finished.")
            .WithColor(Color.Orange)
            .WithCurrentTimestamp()
            .Build();

        await ticketChannel.SendMessageAsync(embed: embed);
        await command.RespondAsync($"✅ Temp ticket created: {ticketChannel.Mention}", ephemeral: true);
    }
}
