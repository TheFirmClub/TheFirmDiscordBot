using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FirmDiscordBot.Services;

public sealed class ManualVerifyOnJoinHandler
{
    private readonly DiscordSocketClient _client;

    // ✅ Categories
    private const ulong OpenManualVerifyCategoryId = 1474419571929907394;

    // ✅ Roles
    private const ulong ManualVerifyRoleId = 1474419863798812752;
    private const ulong NonVerifiedRoleId = 1393664929042530425;
    private const ulong ForumLinkedRoleId = 1467211008610406411;
    private const ulong FirmRobotRoleId = 1398764987685539962;
    private const ulong TheFirmRoleId = 1508057487863971843;

    // ✅ Ping ONLY these roles in the ticket
    //private const ulong GameModeratorRoleId = 1393729574537396355;
    private const ulong DiscordModeratorRoleId = 1393623589122736238;

    // ✅ Still allow these staff roles to view the ticket
    private static readonly ulong[] StaffViewRoleIds =
    {
        //1393729574537396355, // Game Moderator
        1393623589122736238, // Discord Moderator
        1393638449709584434, // Senior Moderator
        1405330877440983130, // Assistant Head Moderator
        1393728468608487594, // Head Moderator
        1393590761953558608  // Senior Management
    };

    // ✅ Announcement channel (game-moderators)
    private const ulong VerifyAnnouncementsChannelId = 1474538794953867337;

    private const int MinAccountAgeDays = 30;

    public ManualVerifyOnJoinHandler(DiscordSocketClient client)
    {
        _client = client;
    }

    public void Register()
    {
        _client.UserJoined += user =>
        {
            // Run in background so gateway is not blocked
            _ = Task.Run(async () =>
            {
                try
                {
                    await OnUserJoinedAsync(user);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("🔥 ManualVerifyOnJoinHandler error:");
                    Console.WriteLine(ex);
                }
            });

            // Immediately return so the gateway thread is never blocked
            return Task.CompletedTask;
        };
    }

    private async Task OnUserJoinedAsync(SocketGuildUser user)
    {
        try
        {
            var accountAge = DateTimeOffset.UtcNow - user.CreatedAt;
            if (accountAge.TotalDays >= MinAccountAgeDays)
                return;

            // Pass 1: remove roles immediately (may miss roles added a moment later)
            await RemoveAllManageableRolesAsync(user, excludeRoleIds: new[] { ManualVerifyRoleId, ForumLinkedRoleId });

            // Add Manual Verification role
            var manualRole = user.Guild.GetRole(ManualVerifyRoleId);
            if (manualRole != null && !user.Roles.Any(r => r.Id == ManualVerifyRoleId))
                await user.AddRoleAsync(manualRole);

            // Create ticket
            await CreateManualVerifyTicketAsync(user, accountAge);

            // ✅ Pass 2: wait briefly, then remove again (fixes auto-role race conditions)
            await Task.Delay(4000);

            var refreshed = user.Guild.GetUser(user.Id);
            if (refreshed != null)
            {
                await RemoveAllManageableRolesAsync(refreshed, excludeRoleIds: new[] { ManualVerifyRoleId, ForumLinkedRoleId });

                // Ensure Non-Verified is gone (explicit)
                var nonVerifiedRole = refreshed.Guild.GetRole(NonVerifiedRoleId);
                if (nonVerifiedRole != null && refreshed.Roles.Any(r => r.Id == NonVerifiedRoleId))
                {
                    try { await refreshed.RemoveRoleAsync(nonVerifiedRole); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Could not remove Non-Verified role from {refreshed.Id}. Likely role hierarchy issue. {ex.Message}");
                    }
                }

                // Ensure Manual Verify role is still applied
                if (manualRole != null && !refreshed.Roles.Any(r => r.Id == ManualVerifyRoleId))
                    await refreshed.AddRoleAsync(manualRole);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 ManualVerifyOnJoinHandler error:");
            Console.WriteLine(ex);
        }
    }

    private static async Task RemoveAllManageableRolesAsync(SocketGuildUser user, IEnumerable<ulong>? excludeRoleIds = null)
    {
        var exclude = excludeRoleIds?.ToHashSet() ?? new HashSet<ulong>();
        var bot = user.Guild.CurrentUser;

        var rolesToRemove = user.Roles
            .Where(r => r.Id != user.Guild.EveryoneRole.Id)
            .Where(r => !exclude.Contains(r.Id))
            .Where(r => !r.IsManaged)
            .Where(r => r.Position < bot.Hierarchy)
            .ToArray();

        if (rolesToRemove.Length > 0)
            await user.RemoveRolesAsync(rolesToRemove);
    }

    private async Task CreateManualVerifyTicketAsync(SocketGuildUser user, TimeSpan accountAge)
    {
        var guild = user.Guild;

        var rand = Random.Shared.Next(1000, 9999);
        var channelName = $"new-verify-{rand}";

        var overwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),

            new(user.Id, PermissionTarget.User,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow)),

            new(FirmRobotRoleId, PermissionTarget.Role,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow,
                    manageMessages: PermValue.Allow,
                    manageChannel: PermValue.Allow,
                    useApplicationCommands: PermValue.Allow)),

            new(TheFirmRoleId, PermissionTarget.Role,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow))
        };

        foreach (var roleId in StaffViewRoleIds)
        {
            overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow)));
        }

        // Create channel (REST channel returned in your version)
        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = OpenManualVerifyCategoryId;
            props.PermissionOverwrites = overwrites;
            props.Topic = $"owner:{user.Id}; type:new_verify; created:{DateTimeOffset.UtcNow:O}; acct_created:{user.CreatedAt:O}";
        });

        // Ping ONLY Game Moderator + Discord Moderator + user
        //var gameMod = guild.GetRole(GameModeratorRoleId);
        var discordMod = guild.GetRole(DiscordModeratorRoleId);
        var pingText = $"{discordMod?.Mention} {user.Mention}".Trim();

        await channel.SendMessageAsync(pingText);

        // User-facing instructions embed
        var instructionsEmbed = new EmbedBuilder()
            .WithTitle("🛡️ New Discord Verification Check")
            .WithColor(Color.Orange)
            .WithDescription(
                $"Hello {user.Mention},\n\n" +

                $"Thank you for joining **PoliceX**. Our system has detected that your Discord account was recently created. " +
                $"As a security precaution, this has automatically triggered a manual verification check to help us protect the community.\n\n" +

                $"🔒 **Step 1 – Manual Discord Verification**\n" +
                $"Please wait for our moderation team to manually verify your access to our Discord.\n\n" +

                $"Once you have passed manual verification, you will be provided with the **Non-Verified** role.\n\n" +

                $"🔗 **Step 2 – Verify via the PoliceX Dashboard**\n" +
                $"After receiving the **Non-Verified** role, please access our Player Dashboard to complete verification:\n" +
                $"https://dashboard.policex.co.uk\n\n" +

                $"Once verified, you will gain access to our Discord channels and be able to join the **PoliceX** FiveM server.\n\n" +

                $"Thank you for your patience."
            )
            .AddField("Account Created (UTC)", $"{user.CreatedAt:yyyy-MM-dd HH:mm}", true)
            .AddField("Account Age", $"{(int)accountAge.TotalDays} days", true)
            .WithFooter(f =>
            {
                f.Text = $"User: {user.Username} ({user.Id})";
                f.IconUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            })
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: instructionsEmbed);

        var newTopic =
            $"owner:{user.Id}; type:new_verify; created:{DateTimeOffset.UtcNow:O}; acct_created:{user.CreatedAt:O}";
        await channel.ModifyAsync(props => props.Topic = newTopic);

        await channel.SendMessageAsync($"👋 {user.Mention} Please complete the FiveM and dashboard steps above.");

        // ✅ Announcement embed to game-moderators channel
        await SendStaffAnnouncementAsync(guild, user, channel, accountAge);
    }

    // ✅ Accept ITextChannel so REST + Socket channels both work
    private async Task SendStaffAnnouncementAsync(SocketGuild guild, SocketGuildUser user, ITextChannel ticketChannel, TimeSpan accountAge)
    {
        try
        {
            // Use client channel lookup to avoid cache issues
            if (_client.GetChannel(VerifyAnnouncementsChannelId) is not IMessageChannel announceChannel)
                return;

            var eb = new EmbedBuilder()
                .WithTitle("🛡️ New Discord Verify Ticket Created")
                .WithColor(Color.Orange)
                .WithDescription(
                    $"A new user verification ticket has been created.\n\n" +
                    $"**User:** {user.Mention} (`{user.Id}`)\n" +
                    $"**Account Created:** {user.CreatedAt:yyyy-MM-dd HH:mm} UTC\n" +
                    $"**Account Age:** {(int)accountAge.TotalDays} days\n\n" +
                    $"**Ticket:** <#{ticketChannel.Id}>\n\n" +
                    $"✅ If auto verify is unsuccessful, check if user has created a character for verification.\n" +
                    $"(Fallback: **/manualverify {user.Id}**)"
                )
                .WithTimestamp(DateTimeOffset.UtcNow);

            await announceChannel.SendMessageAsync(embed: eb.Build());
        }
        catch (Exception ex)
        {
            Console.WriteLine("⚠️ Failed to send verify announcement embed:");
            Console.WriteLine(ex);
        }
    }
}