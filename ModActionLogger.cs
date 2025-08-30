using Discord;
using Discord.WebSocket;
using System;
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

        _client.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
        _client.UserBanned += OnUserBannedAsync;
        _client.UserUnbanned += OnUserUnbannedAsync;
        _client.UserLeft += IgnoreUserLeftAsync;
        _client.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
    }

    private Task IgnoreUserLeftAsync(SocketGuildUser user)
    {
        return Task.CompletedTask;
    }

    private async Task OnGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache, SocketGuildUser after)
    {
        var before = await beforeCache.GetOrDownloadAsync();
        if (before == null) return;

        // Timeout detected
        if (after.TimedOutUntil != null && (before.TimedOutUntil == null || before.TimedOutUntil < after.TimedOutUntil))
        {
            await LogActionAsync(after.Guild, $"⏱️ {after.Mention} was timed out until {after.TimedOutUntil.Value.UtcDateTime}");
        }
    }

    private async Task OnVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user is not SocketGuildUser guildUser) return;

        if (before.IsMuted != after.IsMuted)
        {
            await LogActionAsync(guildUser.Guild, $"{guildUser.Mention} was {(after.IsMuted ? "server-muted" : "unmuted")}");
        }

        if (before.IsDeafened != after.IsDeafened)
        {
            await LogActionAsync(guildUser.Guild, $"{guildUser.Mention} was {(after.IsDeafened ? "server-deafened" : "undeafened")}");
        }
    }

    private async Task OnUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        var mod = await GetModeratorAsync(guild, "Ban");
        await LogActionAsync(guild, $"🚫 {user.Username} was banned by {mod}");
    }

    private async Task OnUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        var mod = await GetModeratorAsync(guild, "Unban");
        await LogActionAsync(guild, $"✅ {user.Username} was unbanned by {mod}");
    }

    private async Task LogActionAsync(SocketGuild guild, string message)
    {
        var channel = guild.GetTextChannel(_modNotesChannelId);
        if (channel != null)
        {
            var embed = new EmbedBuilder()
                .WithTitle("🛡️ Moderation Action")
                .WithDescription(message)
                .WithColor(Color.DarkRed)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }

    private async Task<string> GetModeratorAsync(SocketGuild guild, string actionType)
    {
        var logs = await guild.GetAuditLogsAsync(5).FlattenAsync();

        ActionType type = actionType switch
        {
            "Ban" => ActionType.Ban,
            "Unban" => ActionType.Unban,
            _ => ActionType.Ban
        };

        var entry = logs.FirstOrDefault(l => l.Action == type);
        return entry?.User?.Mention ?? "Unknown Moderator";
    }
}
