using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class ModActionLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _modLogChannelId;

    public ModActionLogger(DiscordSocketClient client, ulong modLogChannelId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _modLogChannelId = modLogChannelId;
    }

    private IMessageChannel? GetLogChannel()
    {
        return _client.GetChannel(_modLogChannelId) as IMessageChannel;
    }

    // Ban event
    public async Task OnUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        var audit = await guild.GetAuditLogsAsync(1, actionType: ActionType.Ban).FlattenAsync();
        var entry = audit.FirstOrDefault();

        string moderator = entry?.User.ToString() ?? "Unknown";

        var embed = new EmbedBuilder()
            .WithTitle($"🔨 User Banned")
            .AddField("User", $"{user.Mention} ({user.Username}#{user.Discriminator})")
            .AddField("Moderator", moderator)
            .WithColor(Color.Red)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await GetLogChannel()?.SendMessageAsync(embed: embed);
    }

    // Unban event
    public async Task OnUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        var audit = await guild.GetAuditLogsAsync(1, actionType: ActionType.Unban).FlattenAsync();
        var entry = audit.FirstOrDefault();

        string moderator = entry?.User.ToString() ?? "Unknown";

        var embed = new EmbedBuilder()
            .WithTitle($"✅ User Unbanned")
            .AddField("User", $"{user.Mention} ({user.Username}#{user.Discriminator})")
            .AddField("Moderator", moderator)
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await GetLogChannel()?.SendMessageAsync(embed: embed);
    }

    // Kick or member left
    public async Task OnUserLeftAsync(SocketGuildUser user)
    {
        var audit = await user.Guild.GetAuditLogsAsync(5, actionType: ActionType.Kick).FlattenAsync();
        var entry = audit.FirstOrDefault(e => e.Target.Id == user.Id);

        string moderator = entry?.User.ToString() ?? "User Left";

        var embed = new EmbedBuilder()
            .WithTitle($"👢 User Left / Kicked")
            .AddField("User", $"{user.Mention} ({user.Username}#{user.Discriminator})")
            .AddField("Moderator", moderator)
            .WithColor(Color.Orange)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await GetLogChannel()?.SendMessageAsync(embed: embed);
    }

    // Member updates (mute, deafen, timeout)
    public async Task OnGuildMemberUpdatedAsync(SocketGuildUser before, SocketGuildUser after)
    {
        if (before.IsMuted != after.IsMuted || before.IsDeafened != after.IsDeafened || before.TimedOutUntil != after.TimedOutUntil)
        {
            string action = "";
            if (before.IsMuted != after.IsMuted)
                action = after.IsMuted ? "Muted" : "Unmuted";
            else if (before.IsDeafened != after.IsDeafened)
                action = after.IsDeafened ? "Deafened" : "Undeafened";
            else if (before.TimedOutUntil != after.TimedOutUntil)
                action = after.TimedOutUntil.HasValue ? $"Timed Out until {after.TimedOutUntil.Value.UtcDateTime}" : "Timeout Removed";

            var audit = await after.Guild.GetAuditLogsAsync(5).FlattenAsync();
            var entry = audit.FirstOrDefault(e => e.Target.Id == after.Id);

            string moderator = entry?.User.ToString() ?? "Unknown";

            var embed = new EmbedBuilder()
                .WithTitle($"🛡️ Member {action}")
                .AddField("User", $"{after.Mention} ({after.Username}#{after.Discriminator})")
                .AddField("Moderator", moderator)
                .WithColor(Color.DarkBlue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await GetLogChannel()?.SendMessageAsync(embed: embed);
        }
    }
}
