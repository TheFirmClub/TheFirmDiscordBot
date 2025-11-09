using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class CommandTicketCommand : ISlashCommand
{
    public string Name => "commandticket";
    public string Description => "Create a command ticket for a user.";

    private readonly ulong _categoryId = 1393627644326838292; // Adjust if needed

    private readonly ulong[] _allowedRoleIds = new ulong[]
    {
        1420513009729802260,
        1420512797191704616,
        1420512528395665569,
        1394651533253017671
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

        // Permission check — only allow if user has one of the allowed roles
        var hasAllowedRole = staffUser.Roles.Any(r => _allowedRoleIds.Contains(r.Id));
        if (!hasAllowedRole)
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

        int rand = new Random().Next(100, 999);
        string channelName = $"command-{rand}";

        // Setup base permissions
        var overwrites = new Overwrite[]
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(guildUser.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)),
            new Overwrite(staffUser.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)),
            new Overwrite(_seniorModeratorRoleId, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
        }.ToList();

        // Add visibility for the initiator's matching role group
        var matchingRoles = staffUser.Roles.Where(r => _allowedRoleIds.Contains(r.Id));
        foreach (var role in matchingRoles)
        {
            overwrites = overwrites.Append(
                new Overwrite(role.Id, PermissionTarget.Role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
            ).ToList();
        }

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = _categoryId;
            props.PermissionOverwrites = overwrites;
        });

        // Build the embed message
        var embed = new EmbedBuilder()
            .WithTitle("📂 Command Ticket")
            .WithColor(Color.Blue)
            .WithDescription(
                $"**This ticket was opened by {staffUser.Mention}** to address a command-related matter involving {guildUser.Mention}.\n\n" +
        
                $"Our goal is to resolve this efficiently and professionally. Please provide any relevant context, information, or concerns so we can assist you properly.\n\n" +

                $"**What to expect:**\n" +
                $"• A staff member from the command team may ask a few follow-up questions.\n" +
                $"• You’ll be able to clarify your side or report any issues.\n" +
                $"• Once resolved, the ticket will be closed or archived by staff.\n\n" +

                $"🔒 *This channel is private and only visible to the relevant staff team, senior moderators, and the involved user.*\n" +
                $"🗂️ *All messages in this ticket may be logged for accountability and training purposes.*")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("Command Support Ticket System")
            .Build();


        await channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.All);
        await command.RespondAsync($"✅ Command ticket created: {channel.Mention}", ephemeral: true);
    }
}
