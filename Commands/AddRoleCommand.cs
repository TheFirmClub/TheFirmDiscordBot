using Discord;
using Discord.Net;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class AddRoleCommand : ISlashCommand
{
    public string Name => "addrole";
    public string Description => "Adds a role to a user";

    private const ulong SeniorManagementRoleId = 1393590761953558608; // Senior Management

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

    private static readonly HashSet<ulong> MedicalLeadershipRoleIds = new()
    {
        1398308435795251302, // COO
        1394460689338208296, // Medical Director
        1394460400988454953, // CMO
    };

    private static readonly HashSet<ulong> AllowedMedicalRoleIds = new()
    {
        1394460780602196079, // Critical Medic
        1394460919152771103, // Advance Paramedic
        1394460987876446218, // Paramedic
    };
    
    private static readonly HashSet<ulong> PoliceLeadershipRoleIds = new()
    {
        1394649657644290078, // Chief Inspector
        1394458024503935006, // Superintendent
        1394457219298492527, // Commissioner
    };

    private static readonly HashSet<ulong> AllowedPoliceRoleIds = new()
    {
        1394459580649574480, // Response
        1394459751688966184, // Roads Policing
        1394460140891013161, // Tactical Firearms
    };

    private static readonly HashSet<ulong> CivilLeadershipRoleIds = new()
    {
        1394461361366368256, // Director of Civil Affairs
        1406295299311272006, // Head of Civil Affairs
        1406295587686453360, // Asst. Head of Civil Affairs
    };

    private static readonly HashSet<ulong> AllowedCivilRoleIds = new()
    {
        1466582702789754950, // Ferrari Crime Family
        1468016850120999166, // E22
        1462261699494285382, // The Black Oath
        1427055886689632336, // Syndicate Gang
        1446426904151457865, // The Section
        1457933411565178932, // Diaz Family
        1469355919510077470, // GSC
        1469654367593566349, // Lost MC
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

        bool isSeniorManagement   = caller.Roles.Any(r => r.Id == SeniorManagementRoleId);
        bool isAssistantOrHeadMod = caller.Roles.Any(r => AssistantOrHeadModRoleIds.Contains(r.Id));
        bool isMedicalLeadership  = caller.Roles.Any(r => MedicalLeadershipRoleIds.Contains(r.Id));
        bool isPoliceLeadership   = caller.Roles.Any(r => PoliceLeadershipRoleIds.Contains(r.Id));
        bool isCivilLeadership    = caller.Roles.Any(r => CivilLeadershipRoleIds.Contains(r.Id));

        if (!isSeniorManagement && !isAssistantOrHeadMod && !isMedicalLeadership && !isPoliceLeadership && !isCivilLeadership)
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

        if (!isSeniorManagement)
        {
            if (isAssistantOrHeadMod && !AllowedModeratorRoleIds.Contains(role.Id))
            {
                await Reply(command, "❌ You can only assign the approved moderator roles.");
                return;
            }

            if (isMedicalLeadership && !AllowedMedicalRoleIds.Contains(role.Id))
            {
                await Reply(command, "❌ You can only assign approved NHS roles (Critical Medic, Advance Paramedic, Paramedic).");
                return;
            }
            
            if (isPoliceLeadership && !AllowedPoliceRoleIds.Contains(role.Id))
            {
                await Reply(command, "❌ You can only assign approved Police roles (Response, Roads Policing, Tactical Firearms).");
                return;
            }

            if (isCivilLeadership && !AllowedCivilRoleIds.Contains(role.Id))
            {
                await Reply(command, "❌ You can only assign approved Civil Affairs roles.");
                return;
            }
        }

        if (targetUser.Roles.Any(r => r.Id == role.Id))
        {
            await Reply(command, $"ℹ️ {targetUser.Mention} already has `{role.Name}`.");
            return;
        }

        if (role.Position >= caller.Hierarchy)
        {
            await Reply(command, "❌ You cannot assign a role that is equal to or higher than your highest role.");
            return;
        }

        if (role.Position >= guild.CurrentUser.Hierarchy)
        {
            await Reply(command, "❌ I cannot assign that role because it's higher than my highest role.");
            return;
        }

        try
        {
            await targetUser.AddRoleAsync(role);
            await Reply(command, $"✅ Added role `{role.Name}` to {targetUser.Mention}.");
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            await Reply(command, "❌ I do not have permission to add that role.");
        }
    }
}
