using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class EmergencyUnbanAllCommand : ISlashCommand
{
    public string Name => "emergencyunbanall";
    public string Description => "Emergency unban all currently banned users.";

    private readonly ulong[] allowedRoles =
    {
        1393590761953558608 // SM Role
    };

    private bool HasPermission(SocketGuildUser user)
        => user.Roles.Any(r => allowedRoles.Contains(r.Id));

    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser guildUser)
        {
            await command.RespondAsync("❌ Guild only command.", ephemeral: true);
            return;
        }

        if (!HasPermission(guildUser))
        {
            await command.RespondAsync("❌ You don’t have permission.", ephemeral: true);
            return;
        }

        await command.RespondAsync("🚨 Emergency unban started. Checking current ban list...", ephemeral: true);

        var guild = guildUser.Guild;

        int success = 0;
        int failed = 0;
        int total = 0;

        try
        {
            var bans = await guild.GetBansAsync(1000).FlattenAsync();
            var bannedUsers = bans.Select(x => x.User.Id).Distinct().ToList();

            total = bannedUsers.Count;

            foreach (var userId in bannedUsers)
            {
                try
                {
                    await guild.RemoveBanAsync(userId);
                    success++;

                    await Task.Delay(750); // helps with rate limits
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"Failed to unban {userId}: {ex.Message}");
                }
            }

            await command.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content =
                    $"✅ Emergency unban finished.\n\n" +
                    $"Current bans found: **{total}**\n" +
                    $"Unbanned: **{success}**\n" +
                    $"Failed: **{failed}**";
            });
        }
        catch (Exception ex)
        {
            await command.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = $"❌ Emergency unban failed: {ex.Message}";
            });
        }
    }
}