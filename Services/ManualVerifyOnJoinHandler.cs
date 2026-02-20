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

    // ✅ Your IDs
    private const ulong OpenManualVerifyCategoryId = 1474419571929907394;
    private const ulong ManualVerifyRoleId = 1474419863798812752;

    // Staff roles (for ticket access / pings)
    private static readonly ulong[] StaffRoleIds =
    {
        1393729574537396355, // Game Moderator
        1393623589122736238, // Discord Moderator
        1393638449709584434, // Senior Moderator
        1405330877440983130, // Assistant Head Moderator
        1393728468608487594, // Head Moderator
        1393590761953558608  // Senior Management
    };

    private const int MinAccountAgeDays = 30;

    public ManualVerifyOnJoinHandler(DiscordSocketClient client)
    {
        _client = client;
    }

    public void Register()
    {
        _client.UserJoined += OnUserJoinedAsync;
    }

    private async Task OnUserJoinedAsync(SocketGuildUser user)
    {
        try
        {
            var accountAge = DateTimeOffset.UtcNow - user.CreatedAt;
            if (accountAge.TotalDays >= MinAccountAgeDays)
                return;

            // 1) Remove all roles we are allowed to remove
            await RemoveAllManageableRolesAsync(user);

            // 2) Add manual verification role
            var manualRole = user.Guild.GetRole(ManualVerifyRoleId);
            if (manualRole != null)
                await user.AddRoleAsync(manualRole);

            // 3) Create the manual verify ticket channel + messages
            await CreateManualVerifyTicketAsync(user, accountAge);
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 ManualVerifyOnJoinHandler error:");
            Console.WriteLine(ex);
        }
    }

    private static async Task RemoveAllManageableRolesAsync(SocketGuildUser user)
    {
        // ✅ Use cached bot member; avoids IGuild explicit interface issues
        var bot = user.Guild.CurrentUser;

        var rolesToRemove = user.Roles
            .Where(r => r.Id != user.Guild.EveryoneRole.Id)
            .Where(r => !r.IsManaged)
            .Where(r => r.Position < bot.Hierarchy)
            .ToArray();

        if (rolesToRemove.Length > 0)
            await user.RemoveRolesAsync(rolesToRemove);
    }

    private static async Task CreateManualVerifyTicketAsync(SocketGuildUser user, TimeSpan accountAge)
    {
        var guild = user.Guild;

        var rand = Random.Shared.Next(1000, 9999);
        var channelName = $"manual-verify-{rand}";

        var overwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),

            new(user.Id, PermissionTarget.User,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow))
        };

        foreach (var roleId in StaffRoleIds)
        {
            overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow)));
        }

        // Create channel
        var channel = await guild.CreateTextChannelAsync(channelName, props =>
        {
            props.CategoryId = OpenManualVerifyCategoryId;
            props.PermissionOverwrites = overwrites;

            // Initial topic (we will overwrite it after sending the status embed so we can store statusmsg:<id>)
            props.Topic = $"owner:{user.Id}; type:manual_verify; created:{DateTimeOffset.UtcNow:O}; acct_created:{user.CreatedAt:O}";
        });

        // Build staff mentions
        var staffMentions = string.Join(" ",
            StaffRoleIds
                .Select(id => guild.GetRole(id))
                .Where(r => r != null)
                .Select(r => r!.Mention)
        );

        // ✅ Ping staff + user (ensures both are notified, and user is visibly "in" the ticket)
        await channel.SendMessageAsync($"{staffMentions} {user.Mention}");

        // User-facing instructions embed
        var instructionsEmbed = new EmbedBuilder()
            .WithTitle("🛡️ Manual Verification Check")
            .WithColor(Color.Orange)
            .WithDescription(
                $"Hello {user.Mention},\n\n" +
                $"Thank you for joining **The Firm**. Our systems have triggered a manual verification check before we can assign you the **Verified** role.\n\n" +

                $"🔗 **Step 1 – Create a Forum Account**\n" +
                $"Please sign up on our forums:\n" +
                $"https://forum.thefirm.club\n\n" +

                $"🔗 **Step 2 – Link Your Discord Account**\n" +
                $"After signing up, link your Discord account here:\n" +
                $"https://forum.thefirm.club/index.php?account/connected-accounts/\n\n" +

                $"Our Moderators will run through the checks and let you know the outcome here.\n\n" +
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

        // ✅ Status embed (this is what /userverified will UPDATE automatically)
        var pendingEmbed = new EmbedBuilder()
            .WithTitle("⏳ Verification Status: Pending")
            .WithColor(Color.Orange)
            .WithDescription(
                "A Moderator will review your account shortly.\n\n" +
                "Once verified, this message will be updated automatically."
            )
            .AddField("Status", "Pending ⏳", true)
            .AddField("Next Step", "Complete forum linking above", true)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        var pendingMsg = await channel.SendMessageAsync(embed: pendingEmbed);

        // ✅ Store the status message ID in the topic so /userverified can edit it later
        var newTopic =
            $"owner:{user.Id}; type:manual_verify; statusmsg:{pendingMsg.Id}; created:{DateTimeOffset.UtcNow:O}; acct_created:{user.CreatedAt:O}";

        await channel.ModifyAsync(props => props.Topic = newTopic);

        // Optional: final plain reminder
        await channel.SendMessageAsync($"👋 {user.Mention} Please complete the forum linking steps above. A Moderator will reply here once checks are complete.");
    }
}