using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class TempTicketCommand : ISlashCommand
{
    public string Name => "tempticket";
    public string Description => "Create a temporary level 2 ticket for a user";

    private readonly ulong _categoryId = 1393627644326838292;

    private readonly ulong[] _seniorModRoleIds = new ulong[]
    {
        1393638449709584434,
        1393590761953558608
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var staffUser = command.User as SocketGuildUser;
        if (!TicketAddRoleCommand.PermissionHelper.IsSeniorModerator(staffUser))
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

        string cleanName = new string(guildUser.Username
            .ToLower()
            .Where(char.IsLetter)
            .ToArray());

        cleanName = cleanName.Length > 10 ? cleanName.Substring(0, 10) : cleanName;
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

        // Embed message
        var embed = new EmbedBuilder()
            .WithTitle($"📌 TEMP Ticket from {staffUser.DisplayName}")
            .WithColor(Color.Gold)
            .WithDescription(
                $"Hi 👋 {guildUser.Mention},\n\n" +
                $"You have been requested in a temp ticket, please do not be alarmed — we are just looking for more information. " +
                $"Please bear with us as we respond in this temp ticket.\n\n" +
                $"🔒 *Please note: A copy of the chat logs will be stored for audit, quality, and training purposes.*\n" +
                $"🔐 *Disclaimer: This ticket and its contents are confidential and should not be shared or discussed outside of this channel.*")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.All);
        await command.RespondAsync($"✅ Temporary ticket created: {channel.Mention}", ephemeral: true);
    }
}
