using Discord.WebSocket;

public class MyInvitesCommand : ISlashCommand
{
    public string Name => "myinvites";
    public string Description => "Show how many people you have invited to this server.";

    private readonly InviteTrackerService _tracker;
    public MyInvitesCommand(InviteTrackerService tracker) => _tracker = tracker;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.GuildId is null) { await command.RespondAsync("Use this in a server.", ephemeral:true); return; }
        var count = _tracker.GetUserTotal(command.GuildId.Value, command.User.Id);
        await command.RespondAsync($"You’ve invited **{count}** member(s) here.", ephemeral:true);
    }
}