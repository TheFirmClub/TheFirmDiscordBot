using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class SendTFUAppCommand : ISlashCommand
{
    public string Name => "sendtfuapp";
    public string Description => "Send TFU application DM to a user";

    // Allowed roles
    private static readonly ulong[] AllowedRoles =
    {
        1393590761953558608, // SM
        1394457219298492527, // Commissioner
        1394458024503935006, // Superintendent
        1394649657644290078, // Chief Inspector
        1394465316817473646, // TFU Inspector
        1394465460392693840  // TFU Sergeant
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: false); // PUBLIC reply

        // Ensure used in server
        if (command.User is not SocketGuildUser caller)
        {
            await Reply(command, "❌ Must be used in a server.");
            return;
        }

        // Role check
        bool allowed = caller.Roles.Any(r => AllowedRoles.Contains(r.Id));
        if (!allowed)
        {
            await Reply(command, "❌ You are not allowed to use this command.");
            return;
        }

        // Get input safely
        var option = command.Data.Options.FirstOrDefault();

        SocketUser? targetUser = null;

        if (option?.Value is SocketUser user)
        {
            targetUser = user;
        }
        else if (option?.Value != null && ulong.TryParse(option.Value.ToString(), out var id))
        {
            targetUser = caller.Guild.GetUser(id);
        }

        if (targetUser == null)
        {
            await Reply(command, "❌ Could not find that user.");
            return;
        }

        // Build embed
        var embed = new EmbedBuilder()
            .WithTitle("🚨 Tactical Firearms Unit Application")
            .WithDescription(
                @"Thank you for your interest in joining the **Tactical Firearms Unit (TFU)**.

You have successfully completed your ride-along and are now eligible to apply for a position within this elite unit.

Please submit your application using the link below:

🔗 https://forum.thefirm.club/index.php?categories/tactical-firearms-unit-application-form.61/

⚠️ **Please note:** Submission of an application does not guarantee entry into TFU.")
            .WithColor(new Color(0xDC2626))
            .WithFooter($"Sent by {caller.DisplayName}")
            .WithCurrentTimestamp()
            .Build();

        try
        {
            await targetUser.SendMessageAsync(embed: embed);

            await Reply(command,
                $"✅ TFU application link has been sent to user ID `{targetUser.Id}`");
        }
        catch
        {
            await Reply(command,
                $"❌ Failed to DM user ID `{targetUser.Id}` (DMs closed).");
        }
    }

    private static Task Reply(SocketSlashCommand command, string text)
        => command.ModifyOriginalResponseAsync(m =>
        {
            m.Content = text;
            m.Embeds = Array.Empty<Embed>();
        });
}