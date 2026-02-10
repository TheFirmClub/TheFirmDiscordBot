using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketClaimCommand : ISlashCommand
{
    public string Name => "ticketclaim";
    public string Description => "Claim a ticket";

    private const ulong LogChannelId = 1394405064520499415;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var moderator = command.User as SocketGuildUser;
        var channel = command.Channel as SocketTextChannel;

        // Safety check
        if (moderator == null || channel == null)
            return;

        // Permission check
        if (!PermissionHelper.IsModerator(moderator))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        // ✅ Prevent double claims
        if (channel.Topic != null && channel.Topic.Contains("Claimed by"))
        {
            await command.RespondAsync("⚠️ This ticket is already claimed.", ephemeral: true);
            return;
        }

        // ✅ Update topic
        await channel.ModifyAsync(x =>
            x.Topic = $"Claimed by {moderator.Id}"
        );

        // ✅ Respond inside ticket
        await command.RespondAsync($"🔒 Ticket claimed by {moderator.Mention}");

        // ✅ Send log embed
        var logChannel = channel.Guild.GetTextChannel(LogChannelId);

        if (logChannel != null)
        {
            var embed = new EmbedBuilder()
                .WithTitle("🔒 Ticket Claimed")
                .AddField("Moderator", moderator.Mention, true)
                .AddField("Ticket", $"{channel.Name} (`{channel.Id}`)", true)
                .WithColor(Color.Blue)
                .WithTimestamp(System.DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: embed);
        }
    }
    public static class PermissionHelper
    {
        public static bool IsModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393729574537396355 ||
                r.Id == 1393623589122736238 ||
                r.Id == 1393638449709584434 ||
                r.Id == 1393590761953558608);
        }

        public static bool IsSeniorModerator(SocketGuildUser user)
        {
            return user.Roles.Any(r =>
                r.Id == 1393638449709584434 ||
                r.Id == 1393590761953558608);
        }
    }
}
