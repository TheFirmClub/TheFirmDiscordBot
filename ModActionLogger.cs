using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class ModActionLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _modNotesChannelId;

    public ModActionLogger(DiscordSocketClient client, ulong modNotesChannelId)
    {
        _client = client;
        _modNotesChannelId = modNotesChannelId;
    }

    private async Task<ITextChannel?> GetLogChannelAsync(SocketGuild guild)
    {
        return guild.GetTextChannel(_modNotesChannelId);
    }

    public async Task OnUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        var logs = await guild.GetAuditLogsAsync(1).FlattenAsync();
        var entry = logs.FirstOrDefault() as RestBanAuditLogEntry;

        var moderator = entry?.User;
        var channel = await GetLogChannelAsync(guild);
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("[BAN]")
            .AddField("User", user.Mention, true)
            .AddField("Moderator", moderator?.Mention ?? "Unknown", true)
            .WithColor(Color.DarkRed)
            .WithCurrentTimestamp();

        await channel.SendMessageAsync(embed: embed.Build());
    }

    public async Task OnUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        var logs = await guild.GetAuditLogsAsync(1).FlattenAsync();
        var entry = logs.FirstOrDefault() as RestUnbanAuditLogEntry;

        var moderator = entry?.User;
        var channel = await GetLogChannelAsync(guild);
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("[UNBAN]")
            .AddField("User", user.Mention, true)
            .AddField("Moderator", moderator?.Mention ?? "Unknown", true)
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        await channel.SendMessageAsync(embed: embed.Build());
    }

    public async Task OnUserLeftAsync(SocketGuildUser user)
    {
        var guild = user.Guild;
        var logs = await guild.GetAuditLogsAsync(1).FlattenAsync();
        var entry = logs.FirstOrDefault();

        string actionType = "";
        SocketUser? moderator = null;

        if (entry is RestKickAuditLogEntry kickEntry && kickEntry.Target.Id == user.Id)
        {
            actionType = "[KICK]";
            moderator = kickEntry.User;
        }
        else
        {
            actionType = "[LEAVE]";
        }

        var channel = await GetLogChannelAsync(guild);
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle(actionType)
            .AddField("User", user.Mention, true);

        if (moderator != null)
            embed.AddField("Moderator", moderator.Mention, true);

        embed.WithColor(actionType == "[LEAVE]" ? Color.LightGrey : Color.DarkOrange)
             .WithCurrentTimestamp();

        await channel.SendMessageAsync(embed: embed.Build());
    }

    public async Task OnGuildMemberUpdatedAsync(SocketGuildUser before, SocketGuildUser after)
    {
        var channel = await GetLogChannelAsync(after.Guild);
        if (channel == null) return;

        if (before.TimedOutUntil != after.TimedOutUntil)
        {
            var logs = await after.Guild.GetAuditLogsAsync(1).FlattenAsync();
            var entry = logs.FirstOrDefault() as RestMemberUpdateAuditLogEntry;

            var moderator = entry?.User;

            var embed = new EmbedBuilder()
                .WithTitle("[TIMEOUT]")
                .AddField("User", after.Mention, true)
                .AddField("Moderator", moderator?.Mention ?? "Unknown", true)
                .AddField("Until", after.TimedOutUntil?.ToString("f") ?? "Removed", true)
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            await channel.SendMessageAsync(embed: embed.Build());
        }
    }
}
