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
        _client = client;
        _modLogChannelId = modLogChannelId;

        _client.UserBanned += OnUserBanned;
        _client.UserUnbanned += OnUserUnbanned;
        _client.GuildMemberUpdated += OnGuildMemberUpdated;
        _client.UserLeft += OnUserLeft; // we'll filter leaves
    }

    private async Task OnUserBanned(SocketUser user, SocketGuild guild)
    {
        var channel = guild.GetTextChannel(_modLogChannelId);
        if (channel == null) return;

        var audit = await guild.GetAuditLogsAsync(1, ActionType.Ban).FlattenAsync();
        var entry = audit.FirstOrDefault();
        string moderator = entry?.User?.Mention ?? "Unknown";

        var embed = new EmbedBuilder()
            .WithTitle("⛔ User Banned")
            .AddField("User", $"{user.Mention} ({user.Id})")
            .AddField("Moderator", moderator)
            .WithColor(Color.Red)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private async Task OnUserUnbanned(SocketUser user, SocketGuild guild)
    {
        var channel = guild.GetTextChannel(_modLogChannelId);
        if (channel == null) return;

        var audit = await guild.GetAuditLogsAsync(1, ActionType.Unban).FlattenAsync();
        var entry = audit.FirstOrDefault();
        string moderator = entry?.User?.Mention ?? "Unknown";

        var embed = new EmbedBuilder()
            .WithTitle("✅ User Unbanned")
            .AddField("User", $"{user.Mention} ({user.Id})")
            .AddField("Moderator", moderator)
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private async Task OnGuildMemberUpdated(SocketGuildUser before, SocketGuildUser after)
    {
        var channel = after.Guild.GetTextChannel(_modLogChannelId);
        if (channel == null) return;

        // Server mute/unmute
        if (before.IsMuted != after.IsMuted)
        {
            string title = after.IsMuted ? "🔇 User Server Muted" : "🔊 User Server Unmuted";
            await SendEmbed(channel, title, after, after.IsMuted ? Color.Orange : Color.Green);
        }

        // Server deaf/undeaf
        if (before.IsDeafened != after.IsDeafened)
        {
            string title = after.IsDeafened ? "🎧 User Server Deafened" : "👂 User Server Undeafened";
            await SendEmbed(channel, title, after, after.IsDeafened ? Color.DarkRed : Color.Green);
        }

        // Timeout applied or lifted
        if (before.CommunicationDisabledUntil != after.CommunicationDisabledUntil)
        {
            string title = after.CommunicationDisabledUntil > DateTimeOffset.UtcNow
                ? "⏱️ User Timed Out"
                : "✅ User Timeout Lifted";

            string until = after.CommunicationDisabledUntil?.UtcDateTime.ToString("u") ?? "N/A";

            var audit = await after.Guild.GetAuditLogsAsync(5, ActionType.MemberUpdate).FlattenAsync();
            var entry = audit.FirstOrDefault(a => a.Target.Id == after.Id && a.Action == ActionType.MemberUpdate);
            string moderator = entry?.User?.Mention ?? "Unknown";

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .AddField("User", $"{after.Mention} ({after.Id})")
                .AddField("Moderator", moderator)
                .AddField("Until", until)
                .WithColor(after.CommunicationDisabledUntil > DateTimeOffset.UtcNow ? Color.Orange : Color.Green)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }

    private async Task OnUserLeft(SocketGuildUser user)
    {
        // Do NOT log normal leaves
    }

    private static async Task SendEmbed(IMessageChannel channel, string title, SocketGuildUser user, Color color)
    {
        var audit = await user.Guild.GetAuditLogsAsync(5).FlattenAsync();
        var entry = audit.FirstOrDefault(a => a.Target.Id == user.Id);
        string moderator = entry?.User?.Mention ?? "Unknown";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .AddField("User", $"{user.Mention} ({user.Id})")
            .AddField("Moderator", moderator)
            .WithColor(color)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }
}
