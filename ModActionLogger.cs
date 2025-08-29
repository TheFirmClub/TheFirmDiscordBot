using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class ModActionLogger
{
    private readonly DiscordSocketClient _client;
    private const ulong LogChannelId = 1394451583709745273; // Mod action log channel

    public ModActionLogger(DiscordSocketClient client)
    {
        _client = client;
        _client.UserBanned += OnUserBanned;
        _client.UserUnbanned += OnUserUnbanned;
        _client.UserLeft += OnUserLeft;
    }

    private async Task OnUserBanned(SocketUser user, SocketGuild guild)
    {
        var log = await guild.GetAuditLogsAsync(1).FirstAsync();
        var entry = log.Entries.FirstOrDefault(e => e.Action == ActionType.Ban);
        if (entry == null) return;

        var moderator = entry.User;
        var reason = entry.Reason ?? "No reason provided";

        await SendLogAsync(guild, $"{user.Username} was **banned** by {moderator.Username}. Reason: {reason}");
    }

    private async Task OnUserUnbanned(SocketUser user, SocketGuild guild)
    {
        var log = await guild.GetAuditLogsAsync(1).FirstAsync();
        var entry = log.Entries.FirstOrDefault(e => e.Action == ActionType.Unban);
        if (entry == null) return;

        var moderator = entry.User;
        var reason = entry.Reason ?? "No reason provided";

        await SendLogAsync(guild, $"{user.Username} was **unbanned** by {moderator.Username}. Reason: {reason}");
    }

    private async Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        var log = await guild.GetAuditLogsAsync(1).FirstAsync();
        var entry = log.Entries.FirstOrDefault(e => e.Action == ActionType.Kick);
        if (entry == null) return;

        var moderator = entry.User;
        var reason = entry.Reason ?? "No reason provided";

        await SendLogAsync(guild, $"{user.Username} was **kicked** by {moderator.Username}. Reason: {reason}");
    }

    private async Task SendLogAsync(SocketGuild guild, string message)
    {
        var channel = guild.GetTextChannel(LogChannelId);
        if (channel != null)
        {
            await channel.SendMessageAsync(message);
        }
    }
}
