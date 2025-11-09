using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class ModTicketCommand : ISlashCommand
{
    public string Name => "modticket";
    public string Description => "Create a moderation ticket for a user";

    // Same category as your temp ticket, per your note "everything works the same"
    private readonly ulong _categoryId = 1393610364511326259;

    // Access: Game Mod + Senior Management
    private readonly ulong[] _allowedRoleIds = new ulong[]
    {
        1393729574537396355, // Game Mod
        1393590761953558608  // Senior Management
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var staffUser = command.User as SocketGuildUser;
        if (!IsAuthorizedMod(staffUser))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        var targetUser = (SocketUser)command.Data.Options.First().Value;
        var guildUser = targetUser as SocketGuildUser;
        if (guildUser == null)
        {
            await command.RespondAsync("❌ Invalid user.", ephemeral: true);
            return;
        }

        var guild = guildUser.Guild;

        // Channel name: mod-1234 (4 random digits)
        int rand = new Random().Next(1000, 9999);
        string channelName = $"mod-{rand}";

        // Base overwrites: hide from everyone; allow target user
        var overwrites = new Overwrite[]
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),

            new Overwrite(guildUser.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
        };

        var perms = overwrites.ToList();

        // Allow Game Mod + Senior Management roles
        foreach (var roleId in _allowedRoleIds)
        {
            perms.Add(new Overwrite(roleId, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }

        // (Optional) Ensure the command invoker has access even if they somehow lack the roles
        // Comment out if you strictly want role-gated access.
        perms.Add(new Overwrite(staffUser.Id, PermissionTarget.User,
            new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = _categoryId;
            props.PermissionOverwrites = perms;
        });

        // Moderation Ticket embed (different content from temp ticket)
        var embed = new EmbedBuilder()
            .WithTitle("🛡️ Moderation Ticket")
            .WithColor(Color.DarkRed)
            .WithDescription(
                $"This ticket was opened by {staffUser.Mention}, a member of the moderation team, to address a moderation matter involving a recent incident.\n\n" +
                $"Our goal is to understand what happened, hear your perspective, and clarify any relevant guidelines.\n\n" +
                $"**What to expect:**\n" +
                $"• The moderator above may ask questions to understand the situation clearly.\n" +
                $"• You will have the opportunity to explain your side.\n" +
                $"• Any outcomes or next steps will be explained once the discussion is complete.\n\n" +
                $"🔒 *Please note: A copy of this conversation may be logged for audit, quality, and training purposes.*\n" +
                $"🔐 *This ticket is confidential and should not be shared outside of this channel.*")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.All);
        await command.RespondAsync($"✅ Moderation ticket created: {channel.Mention}", ephemeral: true);
    }

    private bool IsAuthorizedMod(SocketGuildUser user)
    {
        if (user == null) return false;
        var userRoleIds = user.Roles.Select(r => r.Id);
        return _allowedRoleIds.Any(id => userRoleIds.Contains(id));
    }
}
