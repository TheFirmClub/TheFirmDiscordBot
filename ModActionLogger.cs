using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

public class ModActionLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _modNotesChannelId;

    public ModActionLogger(DiscordSocketClient client, ulong modNotesChannelId)
    {
        _client = client;
        _modNotesChannelId = modNotesChannelId;

        // Hook events
        _client.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
        _client.GuildMemberUpdated += TrackTimeoutAsync; // Optional separate handler for timeout
        _client.UserBanned += OnUserBannedAsync;
        _client.UserUnbanned += OnUserUnbannedAsync;
        _client.UserLeft += IgnoreUserLeftAsync; // We'll ignore leaves
        _client.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
    }

    private Task IgnoreUserLeftAsync(SocketUser user)
    {
        // Do nothing, intentionally skipping leave logging
        return Task.CompletedTask;
    }

    private async Task OnGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache, SocketGuildUser after)
    {
        var before = await beforeCache.GetOrDownloadAsync();
        if (before == null) return;

        // Example: role changes
        if (before.Roles.Count != after.Roles.Count)
        {
            await LogActionAsync(after.Guild, $"Roles updated for {after.Mention}");
        }
    }

    private async Task TrackTimeoutAsync(Cacheable<SocketGuildUser, ulong> beforeCache, SocketGuildUser after)
    {
        var before = await beforeCache.GetOrDownloadAsync();
        if (before == null) return;

        // Timeout detected
        if (after.TimedOutUntil != null && (before.TimedOutUntil == null || before.TimedOutUntil < after.TimedOutUntil))
        {
            var mod = await GetModeratorAsync(after.Guild); // You can implement a method to fetch the moderator responsible
            await LogActionAsync(after.Guild, $"⏱️ {after.Mention} was timed out until {after.TimedOutUntil.Value.UtcDateTime} by {mod}");
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
        var mod = await GetModeratorAsync(guild);
        await LogActionAsync(guild, $"🚫 {user.Username} was banned by {mod}");
    }

    private async Task OnUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        var mod = await GetModeratorAsync(guild);
        await LogActionAsync(guild, $"✅ {user.Username} was unbanned by {mod}");
    }

    private async Task LogActionAsync(SocketGuild guild, string message)
    {
        var channel = guild.GetTextChannel(_modNotesChannelId);
        if (channel != null)
        {
            try
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🛡️ Moderation Action")
                    .WithDescription(message)
                    .WithColor(Color.DarkRed)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await channel.SendMessageAsync(embed: embed);
            }
            catch { /* ignore errors */ }
        }
    }

    private async Task<string> GetModeratorAsync(SocketGuild guild)
    {
        // Optional: fetch the last audit log entry for action type and return the responsible moderator
        var logs = await guild.GetAuditLogsAsync(1).FlattenAsync();
        var entry = logs.FirstOrDefault();
        return entry?.User?.Mention ?? "Unknown Moderator";
    }
}
