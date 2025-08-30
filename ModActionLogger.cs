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
        _client.UserUpdated += OnUserUpdated;
    }

    private async Task OnUserBanned(SocketUser user, SocketGuild guild)
    {
        var channel = guild.GetTextChannel(_modLogChannelId);
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("🔨 User Banned")
            .AddField("User", $"{user.Mention} ({user.Id})")
            .WithColor(Color.Red)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private async Task OnUserUnbanned(SocketUser user, SocketGuild guild)
    {
        var channel = guild.GetTextChannel(_modLogChannelId);
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("✅ User Unbanned")
            .AddField("User", $"{user.Mention} ({user.Id})")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private async Task OnUserUpdated(SocketUser before, SocketUser after)
    {
        // For moderation logging like nickname changes
        if (before.Username != after.Username)
        {
            var guildUser = after as SocketGuildUser;
            if (guildUser == null) return;

            var channel = guildUser.Guild.GetTextChannel(_modLogChannelId);
            if (channel == null) return;

            var embed = new EmbedBuilder()
                .WithTitle("✏️ User Updated")
                .AddField("User", $"{guildUser.Mention} ({guildUser.Id})")
                .AddField("Old Username", before.Username)
                .AddField("New Username", after.Username)
                .WithColor(Color.Orange)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }
}
