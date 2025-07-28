using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class TempTicketCommand : ISlashCommand
{
    public string Name => "tempticket";
    public string Description => "Create a temporary level 2 ticket for a user";

    // IDs
    private readonly ulong _categoryId = 1393627644326838292;
    private readonly ulong[] _seniorModRoleIds = new ulong[]
    {
        1393638449709584434, // senior mod
        1393590761953558608  // another senior mod role
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!TicketAddRoleCommand.PermissionHelper.IsModerator(user))
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
        string cleanName = guildUser.Username.ToLower().Replace(" ", "").Replace("#", "").Replace(".", "");
        int rand = new Random().Next(100, 999);
        string channelName = $"temp-{cleanName}-{rand}";

        var overwrites = new Overwrite[]
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),

            new Overwrite(guildUser.Id, PermissionTarget.User,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
        };

        var perms = overwrites.ToList();

        foreach (var roleId in _seniorModRoleIds)
        {
            perms.Add(new Overwrite(roleId, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));
        }

        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = _categoryId;
            props.PermissionOverwrites = perms;
        });

        await channel.SendMessageAsync($"📝 Temporary ticket created for {guildUser.Mention}.\n" +
                                       $"This channel is only visible to senior moderators and the user.");

        await command.RespondAsync($"✅ Ticket created: {channel.Mention}", ephemeral: true);
    }
}
