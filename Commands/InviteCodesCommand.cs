using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class InviteCodesCommand : ISlashCommand
{
    public string Name => "invitecodes";
    public string Description => "Show per-code invite breakdown for a user (defaults to you).";

    private readonly InviteTrackerService _tracker;
    public InviteCodesCommand(InviteTrackerService tracker) => _tracker = tracker;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.GuildId is null)
        {
            await command.RespondAsync("Use this in a server.", ephemeral: true);
            return;
        }

        var guild = (command.Channel as SocketGuildChannel)?.Guild
                    ?? (command.User as SocketGuildUser)?.Guild;

        ulong targetId = command.User.Id;
        var opt = command.Data.Options?.FirstOrDefault(o => o.Name == "user")?.Value;
        if (opt is SocketGuildUser u) targetId = u.Id;

        var items = _tracker.GetPerCodeBreakdown(command.GuildId.Value, targetId);
        if (items.Count == 0)
        {
            await command.RespondAsync("No per-code data for that user yet.", ephemeral: true);
            return;
        }

        var total = items.Sum(x => x.Count);
        var desc = string.Join("\n", items.Select(x => $"`{x.Code}` — **{x.Count}**"));
        var userMention = guild?.GetUser(targetId)?.Mention ?? $"<@{targetId}>";

        var embed = new EmbedBuilder()
            .WithTitle("Invite Codes Breakdown")
            .WithDescription($"{userMention} total: **{total}**\n\n{desc}")
            .WithCurrentTimestamp()
            .Build();

        await command.RespondAsync(embed: embed);
    }
}