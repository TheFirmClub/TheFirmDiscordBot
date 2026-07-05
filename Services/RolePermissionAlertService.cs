using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class RolePermissionAlertService
{
    private readonly DiscordSocketClient _client;

    private const ulong AlertChannelId = 1393597248495030272UL;
    private const ulong SeniorManagementRoleId = 1393590761953558608UL;

    // Roles you specifically want to monitor, even if permissions change later
    private static readonly HashSet<ulong> KnownModerationRoleIds = new()
    {
        1393728468608487594UL, // Head Moderator
        1405330877440983130UL, // Assistant Head Moderator
        1393638449709584434UL, // Senior Moderator
        1393623589122736238UL  // Discord Moderator
    };

    public RolePermissionAlertService(DiscordSocketClient client)
    {
        _client = client;
    }

    public void Register()
    {
        _client.GuildMemberUpdated -= OnGuildMemberUpdatedAsync;
        _client.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
    }

    private async Task OnGuildMemberUpdatedAsync(
        Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser afterUser)
    {
        try
        {
            var beforeUser = beforeCache.HasValue
                ? beforeCache.Value
                : await beforeCache.GetOrDownloadAsync();

            if (beforeUser == null)
                return;

            var beforeRoleIds = beforeUser.Roles.Select(r => r.Id).ToHashSet();

            var addedRoles = afterUser.Roles
                .Where(role => !beforeRoleIds.Contains(role.Id))
                .ToList();

            if (!addedRoles.Any())
                return;

            var sensitiveAddedRoles = addedRoles
                .Where(IsSensitiveRole)
                .ToList();

            if (!sensitiveAddedRoles.Any())
                return;

            var alertChannel = afterUser.Guild.GetTextChannel(AlertChannelId);
            if (alertChannel == null)
                return;

            var rolesText = string.Join("\n", sensitiveAddedRoles.Select(role =>
                $"• {role.Mention} (`{role.Id}`)\n  Permissions: {GetSensitivePermissionsText(role)}"
            ));

            var embed = new EmbedBuilder()
                .WithTitle("🚨 Sensitive Role Assigned")
                .WithColor(Color.Red)
                .WithDescription(
                    $"A role with moderation, kick, ban, admin, or management permissions has been assigned to a member."
                )
                .AddField("User", $"{afterUser.Mention}\n`{afterUser.Id}`", true)
                .AddField("Username", afterUser.Username, true)
                .AddField("Role(s) Added", rolesText, false)
                .WithFooter("Please review this role assignment immediately.")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            var allowedMentions = new AllowedMentions();
            allowedMentions.RoleIds.Add(SeniorManagementRoleId);

            await alertChannel.SendMessageAsync(
                text: $"<@&{SeniorManagementRoleId}> Sensitive role assignment detected.",
                embed: embed,
                allowedMentions: allowedMentions
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RolePermissionAlertService] Error: {ex}");
        }
    }

    private static bool IsSensitiveRole(SocketRole role)
    {
        var perms = role.Permissions;

        return KnownModerationRoleIds.Contains(role.Id)
            || perms.Administrator
            || perms.BanMembers
            || perms.KickMembers
            || perms.ModerateMembers
            || perms.ManageMessages
            || perms.ManageRoles
            || perms.ManageGuild;
    }

    private static string GetSensitivePermissionsText(SocketRole role)
    {
        var perms = role.Permissions;
        var found = new List<string>();

        if (KnownModerationRoleIds.Contains(role.Id))
            found.Add("Known moderation role");

        if (perms.Administrator)
            found.Add("Administrator");

        if (perms.BanMembers)
            found.Add("Ban Members");

        if (perms.KickMembers)
            found.Add("Kick Members");

        if (perms.ModerateMembers)
            found.Add("Moderate Members");

        if (perms.ManageMessages)
            found.Add("Manage Messages");

        if (perms.ManageRoles)
            found.Add("Manage Roles");

        if (perms.ManageGuild)
            found.Add("Manage Server");

        return found.Count == 0 ? "Unknown" : string.Join(", ", found);
    }
}