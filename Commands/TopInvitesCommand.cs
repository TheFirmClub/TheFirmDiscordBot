using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TopInvitesCommand : ISlashCommand
{
    public string Name => "topinvites";
    public string Description => "Show the top inviters in this server.";

    private readonly InviteTrackerService _tracker;
    public TopInvitesCommand(InviteTrackerService tracker) => _tracker = tracker;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.GuildId is null)
        {
            await command.RespondAsync("Use this in a server.", ephemeral: true);
            return;
        }

        var guild = (command.Channel as SocketGuildChannel)?.Guild
                    ?? (command.User as SocketGuildUser)?.Guild;

        int take = 10;
        var opt = command.Data.Options?.FirstOrDefault(o => o.Name == "count")?.Value;
        if (opt is long l && l >= 1 && l <= 25) take = (int)l;

        var top = _tracker.GetTopInviters(command.GuildId.Value, take);
        if (top.Count == 0)
        {
            await command.RespondAsync("No invite data yet.", ephemeral: true);
            return;
        }

        var lines = top.Select((x, i) =>
        {
            var user = guild?.GetUser(x.UserId);
            var name = user?.Mention ?? $"<@{x.UserId}>";
            return $"{i + 1}. {name} — **{x.Count}**";
        });

        var embed = new EmbedBuilder()
            .WithTitle("Top Inviters")
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await command.RespondAsync(embed: embed);
    }
}