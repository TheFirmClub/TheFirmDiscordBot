using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

public class TicketRestrictCommand : ISlashCommand
{
    private const ulong FIRM_ROBOT_ROLE_ID = 1398764987685539962UL; // Firm Robot / bot role
    public string Name => "ticketrestrict";
    public string Description => "Restrict this ticket to a specific role";

    // Base mod roles
    private static readonly ulong GAME_MOD = 1393729574537396355;
    private static readonly ulong DISCORD_MOD = 1393623589122736238;

    // Additional roles
    private static readonly ulong SENIOR_MGMT = 1393590761953558608; // Admin, access to all
    private static readonly ulong SENIOR_MOD  = 1393638449709584434;
    private static readonly ulong HEAD_MOD    = 1393728468608487594;
    private static readonly ulong ASST_HEAD   = 1405330877440983130;

    private readonly ulong _restrictedCategoryId = 1393627644326838292;

    private static readonly ulong[] BASE_MODS = new[] { GAME_MOD, DISCORD_MOD };
    private static readonly ulong[] ALL_MODS  = new[] { GAME_MOD, DISCORD_MOD, SENIOR_MOD, HEAD_MOD, ASST_HEAD };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (!TicketAddRoleCommand.PermissionHelper.IsModerator(user))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketTextChannel channel)
        {
            await command.RespondAsync("❌ This command must be used in a ticket channel.", ephemeral: true);
            return;
        }

        if (command.Data.Options == null || !command.Data.Options.Any())
        {
            await command.RespondAsync("❌ You must mention a role to restrict to.", ephemeral: true);
            return;
        }

        await command.DeferAsync(ephemeral: true);

        var targetRole = (SocketRole)command.Data.Options.First().Value;
        
        var permissionService = new TicketPermissionService();
        var ticketOwnerId = await permissionService.GetOwnerAsync(channel.Id);

        if (channel.CategoryId != _restrictedCategoryId)
        {
            await channel.ModifyAsync(props => props.CategoryId = _restrictedCategoryId);
        }

        // 🚫 STEP 1: DENY everyone FIRST (prevents exposure)
        await channel.AddPermissionOverwriteAsync(
            channel.Guild.EveryoneRole,
            new OverwritePermissions(viewChannel: PermValue.Deny)
        );

        // ❌ STEP 2: Remove ALL other overwrites
        foreach (var overwrite in channel.PermissionOverwrites.ToArray())
        {
            if (overwrite.TargetType == PermissionTarget.Role)
            {
                // Skip @everyone (already handled)
                if (overwrite.TargetId == channel.Guild.EveryoneRole.Id)
                    continue;

                if (overwrite.TargetId == FIRM_ROBOT_ROLE_ID)
                    continue;

                var role = channel.Guild.GetRole(overwrite.TargetId);
                if (role != null)
                    await channel.RemovePermissionOverwriteAsync(role);
            }
            else if (overwrite.TargetType == PermissionTarget.User)
            {
                if (ticketOwnerId.HasValue && overwrite.TargetId == ticketOwnerId.Value)
                    continue;

                var member = channel.Guild.GetUser(overwrite.TargetId);
                if (member != null)
                    await channel.RemovePermissionOverwriteAsync(member);
            }
        }

        // === RULES BRANCHING ===

        var senModRole = channel.Guild.GetRole(SENIOR_MOD);
        var headModRole = channel.Guild.GetRole(HEAD_MOD);
        var asstHeadRole = channel.Guild.GetRole(ASST_HEAD);
        var seniorMgmtRole = channel.Guild.GetRole(SENIOR_MGMT);
        
        if (senModRole == null || headModRole == null || asstHeadRole == null)
        {
            await command.FollowupAsync("❌ Role hierarchy misconfigured.", ephemeral: true);
            return;
        }

        // Helper
        async Task AllowRole(SocketRole role)
        {
            if (role != null)
            {
                await channel.AddPermissionOverwriteAsync(role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
            }
        }

        async Task DenyRole(SocketRole role)
        {
            if (role != null)
            {
                await channel.AddPermissionOverwriteAsync(role,
                    new OverwritePermissions(viewChannel: PermValue.Deny));
            }
        }

        List<ulong> allowedRoles;

        // 🔹 SM → only SM
        if (targetRole.Id == SENIOR_MGMT)
        {
            allowedRoles = new List<ulong>
            {
                SENIOR_MGMT
            };
        }

        // 🔹 AHM → AHM + HM
        else if (targetRole.Id == ASST_HEAD)
        {
            allowedRoles = new List<ulong>
            {
                ASST_HEAD,
                HEAD_MOD
            };
        }

        // 🔹 HM → only HM
        else if (targetRole.Id == HEAD_MOD)
        {
            allowedRoles = new List<ulong>
            {
                HEAD_MOD
            };
        }

        // 🔹 Senior Mod → SM + AHM + HM
        else if (targetRole.Id == SENIOR_MOD)
        {
            allowedRoles = new List<ulong>
            {
                SENIOR_MOD,
                ASST_HEAD,
                HEAD_MOD
            };
        }

        // 🔹 ABOVE AHM (e.g. CI)
        else if (targetRole.Position > asstHeadRole.Position)
        {
            allowedRoles = new List<ulong>
            {
                targetRole.Id,   // ✅ IMPORTANT
                ASST_HEAD,
                HEAD_MOD
            };
        }

        // 🔹 BELOW Senior Mod
        else
        {
            allowedRoles = new List<ulong>
            {
                targetRole.Id,   // ✅ IMPORTANT
                SENIOR_MOD,
                ASST_HEAD,
                HEAD_MOD
            };
        }

        // ✅ Apply roles
        foreach (var roleId in allowedRoles)
        {
            var role = channel.Guild.GetRole(roleId);
            if (role != null)
            {
                await channel.AddPermissionOverwriteAsync(role,
                    new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow
                    ));
            }
        }
        
        // ✅ Re-apply Firm Robot permissions after the restriction reset
        var firmRobotRole = channel.Guild.GetRole(FIRM_ROBOT_ROLE_ID);
        if (firmRobotRole != null)
        {
            await channel.AddPermissionOverwriteAsync(firmRobotRole,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow,
                    manageMessages: PermValue.Allow,
                    manageChannel: PermValue.Allow,
                    useApplicationCommands: PermValue.Allow
                ));
        }

        if (ticketOwnerId.HasValue)
        {
            var ticketOwner = channel.Guild.GetUser(ticketOwnerId.Value);
            if (ticketOwner != null)
            {
                await channel.AddPermissionOverwriteAsync(
                    ticketOwner,
                    new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow
                    )
                );
            }
        }

        await channel.SendMessageAsync($"{targetRole.Mention} 🔒 This ticket has been restricted by {user.Mention}.");

        var logChannel = channel.Guild.GetTextChannel(1394405064520499415);
        if (logChannel != null)
        {
            var logEmbed = new EmbedBuilder()
                .WithTitle("🔐 Ticket Restricted")
                .AddField("Restricted By", user.Mention, true)
                .AddField("Restricted To", targetRole.Mention, true)
                .AddField("Channel", $"{channel.Name} (`{channel.Id}`)", false)
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await logChannel.SendMessageAsync(embed: logEmbed);
        }

        await permissionService.UpdateRolesAsync(channel.Id, new[] { targetRole.Id });
        
        await command.FollowupAsync(
            $"✅ Restricted this ticket to {targetRole.Mention}.",
            ephemeral: true
        );
    }
}
