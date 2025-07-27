using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class RoleLogger
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _logChannelId;

    public RoleLogger(DiscordSocketClient client, ulong logChannelId)
    {
        _client = client;
        _logChannelId = logChannelId;

        _client.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
    }

    private async Task OnGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache, SocketGuildUser after)
    {
        var before = await beforeCache.GetOrDownloadAsync();

        var addedRoles = after.Roles.Except(before.Roles).ToList();
        var removedRoles = before.Roles.Except(after.Roles).ToList();

        if (!addedRoles.Any() && !removedRoles.Any()) return;

        var channel = _client.GetChannel(_logChannelId) as IMessageChannel;
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("🔁 Role Update")
            .WithColor(Color.Teal)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter(footer => footer.Text = "Role Logger")
            .WithThumbnailUrl(after.GetAvatarUrl() ?? after.GetDefaultAvatarUrl());

        embed.WithDescription($"**User:** {after.Mention} ({after.Username}#{after.Discriminator})");

        if (addedRoles.Any())
        {
            embed.AddField("➕ Roles Added", string.Join(", ", addedRoles.Select(r => r.Mention)), true);
        }

        if (removedRoles.Any())
        {
            embed.AddField("➖ Roles Removed", string.Join(", ", removedRoles.Select(r => r.Mention)), true);
        }

        await channel.SendMessageAsync(embed: embed.Build());
    }
}
