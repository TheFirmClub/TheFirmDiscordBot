using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class CommandTicketCommand : ISlashCommand
{
    public string Name => "commandticket";
    public string Description => "Create a command ticket for a user.";

    private readonly ulong _categoryId = 1393627644326838292;

    private readonly ulong[] _allowedRoleIds = new ulong[]
    {
        1394459419156418730, // Response Inspector
        1394650413361533009, // Roads Inspector
        1394465316817473646, // TFU Inspector
        1394649657644290078, // Chief Inspector
        1398308435795251302, // Chief Operations Officer
        1406295587686453360, // Asst. Head of Civil
        1394651533253017671, // Content Coordinator
        1393590761953558608  // Senior Management
    };

    private readonly ulong _seniorModeratorRoleId = 1393638449709584434;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var staffUser = command.User as SocketGuildUser;
        if (staffUser == null)
        {
            await command.RespondAsync("❌ Invalid user context.", ephemeral: true);
            return;
        }

        if (!staffUser.Roles.Any(r => _allowedRoleIds.Contains(r.Id)))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        // Get the mentioned user
        var targetUser = (SocketUser)command.Data.Options.First().Value;
        var guildUser = targetUser as SocketGuildUser;
        if (guildUser == null)
        {
            await command.RespondAsync("❌ Invalid target user.", ephemeral: true);
            return;
        }

        var guild = guildUser.Guild;

        int rand = new Random().Next(1000, 9990);
        string channelName = $"command-{rand}";

        // Permissions
        var overwrites = new Overwrite[]
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),

            new Overwrite(guildUser.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)),

            new Overwrite(_seniorModeratorRoleId, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
        }.ToList();

        // Add initiator's matching roles
        foreach (var role in staffUser.Roles.Where(r => _allowedRoleIds.Contains(r.Id)))
        {
            overwrites.Add(new Overwrite(role.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = _categoryId;
            props.PermissionOverwrites = overwrites;
        });

        // Build embed
        var embed = new EmbedBuilder()
            .WithTitle("📂 Command Ticket")
            .WithColor(Color.Blue)
            .WithDescription(
                $"**This ticket was opened by {staffUser.Mention}** to address a command-related matter involving {guildUser.Mention}.\n\n" +
                $"Our goal is to resolve this efficiently and professionally. Please provide any relevant context or concerns.\n\n" +
                $"**What to expect:**\n" +
                $"• Staff may ask some follow-up questions.\n" +
                $"• You’ll be able to explain your side or report issues.\n" +
                $"• Ticket will be closed or archived when resolved.\n\n" +
                $"🔒 *Please note: A copy of this conversation may be logged for audit, quality, and training purposes.*\n" +
                $"🔐 *This ticket is confidential and should not be shared outside of this channel.*")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        // Ping only the target user in content, not embed
        var allowed = new AllowedMentions
        {
            AllowedTypes = AllowedMentionTypes.None
        };
        allowed.UserIds.Add(guildUser.Id);

        await channel.SendMessageAsync(
            text: $"{guildUser.Mention}",
            embed: embed,
            allowedMentions: allowed
        );

        await command.RespondAsync($"✅ Command ticket created: {channel.Mention}", ephemeral: true);
    }
}
