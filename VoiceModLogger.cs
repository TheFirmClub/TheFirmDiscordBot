using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class VoiceModLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _logChannelId;

    public VoiceModLogger(DiscordSocketClient client, ulong logChannelId)
    {
        _client = client;
        _logChannelId = logChannelId;

        _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
    }

    private async Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user is not SocketGuildUser guildUser) return;

        var guild = guildUser.Guild;
        var logChannel = guild.GetTextChannel(_logChannelId);
        if (logChannel == null) return;

        // --- Voice Channel Move (ignore joins/leaves) ---
        if (before.VoiceChannel != after.VoiceChannel && before.VoiceChannel != null && after.VoiceChannel != null)
        {
            string from = before.VoiceChannel.Name;
            string to = after.VoiceChannel.Name;

            var embed = new EmbedBuilder()
                .WithTitle("🎧 User Moved")
                .WithDescription($"{guildUser.Mention} was moved from **{from}** to **{to}**")
                .WithColor(Color.Orange)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: embed);
        }

        // --- Server Mute/Unmute (ignore self-mutes) ---
        if (before.IsMuted != after.IsMuted && !after.IsSelfMuted)
        {
            string action = after.IsMuted ? "🔇 Muted" : "🔊 Unmuted";

            var embed = new EmbedBuilder()
                .WithTitle("🎧 Voice Moderation")
                .WithDescription($"{guildUser.Mention} was **{action}**")
                .WithColor(after.IsMuted ? Color.Red : Color.Green)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: embed);
        }

        // --- Server Deafen/Undeafen (ignore self-deafens) ---
        if (before.IsDeafened != after.IsDeafened && !after.IsSelfDeafened)
        {
            string action = after.IsDeafened ? "🔇 Deafened" : "🔊 Undeafened";

            var embed = new EmbedBuilder()
                .WithTitle("🎧 Voice Moderation")
                .WithDescription($"{guildUser.Mention} was **{action}**")
                .WithColor(after.IsDeafened ? Color.Red : Color.Green)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: embed);
        }
    }
}
