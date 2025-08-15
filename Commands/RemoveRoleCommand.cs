using Discord;
using Discord.Net;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class RemoveRoleCommand : ISlashCommand
{
    public string Name => "removerole";
    public string Description => "Removes a role from a user";

    private const ulong SeniorManagementRoleId = 1393590761953558608; // Senior Management

    // Only removable by Assistant Head Mod / Head Mod via this command
    private static readonly HashSet<ulong> AllowedModeratorRoleIds = new()
    {
        1393729574537396355, // Game Moderator
        1393623589122736238, // Discord Moderator
        1393638449709584434, // Senior Moderator
    };

    private static readonly HashSet<ulong> AssistantOrHeadModRoleIds = new()
    {
        1405330877440983130, // Assistant Head Moderator
        1393728468608487594, // Head Moderator
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        static Task Reply(SocketSlashCommand cmd, string text)
            => cmd.ModifyOriginalResponseAsync(m => m.Content = text);

        if (command.User is not SocketGuildUser caller)
        {
            await Reply(command, "❌ This command must be used in a server.");
            return;
        }

        var guild = (command.Channel as SocketGuildChannel)?.Guild;
        if (guild == null)
        {
            await Reply(command, "❌ This command must be used in a server.");
            return;
        }

        bool isSeniorManagement = caller.Roles.Any(r => r.Id == SeniorManagementRoleId);
        bool isAssistantOrHeadMod = caller.Roles.Any(r => AssistantOrHeadModRoleIds.Contains(r.Id));

        // Only allow if they have one of the whitelisted roles
        if (!isSeniorManagement && !isAssistantOrHeadMod)
        {
            await Reply(command, "❌ You are not allowed to use this command.");
            return;
        }

        var targetUser = command.Data.Options.FirstOrDefault(o => o.Name == "user")?.Value as SocketGuildUser;
        var role = command.Data.Options.FirstOrDefault(o => o.Name == "role")?.Value as SocketRole;

        if (targetUser == null)
        {
            await Reply(command, "❌ Please specify a valid user.");
            return;
        }

        if (role == null)
        {
            await Reply(command, "❌ Please specify a valid role.");
            return;
        }

        // Restrict Assistant/Head Mod to only the allowed moderator roles
        if (isAssistantOrHeadMod && !AllowedModeratorRoleIds.Contains(role.Id))
        {
            await Reply(command, "❌ You can only remove the approved moderator roles.");
            return;
        }

        if (!targetUser.Roles.Any(r => r.Id == role.Id))
        {
            await Reply(command, $"ℹ️ {targetUser.Mention} does not have `{role.Name}`.");
            return;
        }

        // Keep hierarchy checks to avoid Discord API errors
        if (role.Position >= caller.Hierarchy)
        {
            await Reply(command, "❌ You cannot remove a role that is equal to or higher than your highest role.");
            return;
        }

        if (role.Position >= guild.CurrentUser.Hierarchy)
        {
            await Reply(command, "❌ I cannot remove that role because it's higher than my highest role.");
            return;
        }

        try
        {
            await targetUser.RemoveRoleAsync(role);
            await Reply(command, $"✅ Removed role `{role.Name}` from {targetUser.Mention}.");
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            await Reply(command, "❌ I do not have permission to remove that role.");
        }
    }
}
