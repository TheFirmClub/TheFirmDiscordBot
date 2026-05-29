using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class UnbanRecentCommand : ISlashCommand
{
    public string Name => "unbanrecent";
    public string Description => "Unban everyone banned within the last 12 hours.";

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
            await command.RespondAsync("❌ Command can only be used in a guild.", ephemeral: true);
            return;
        }

        if (!HasPermission(guildUser))
        {
            await command.RespondAsync("❌ You don’t have permission to use this command.", ephemeral: true);
            return;
        }

        await command.RespondAsync("⏳ Checking bans from the last 12 hours...", ephemeral: true);

        try
        {
            var guild = guildUser.Guild;
            var cutoff = DateTimeOffset.UtcNow.AddHours(-12);

            var allLogs = new List<RestAuditLogEntry>();
            ulong? beforeId = null;

            while (true)
            {
                var batch = await guild.GetAuditLogsAsync(
                    limit: 100,
                    beforeId: beforeId,
                    actionType: ActionType.Ban
                ).FlattenAsync();

                var entries = batch.ToList();

                if (entries.Count == 0)
                    break;

                allLogs.AddRange(entries);

                beforeId = entries.Last().Id;

                if (entries.Last().CreatedAt < cutoff)
                    break;
            }

            var currentBans = await guild.GetBansAsync().FlattenAsync();

            var currentlyBannedIds = currentBans
                .Select(x => x.User.Id)
                .ToHashSet();

            var userIds = allLogs
                .Where(x => x.CreatedAt >= cutoff)
                .Select(x =>
                {
                    var data = x.Data as BanAuditLogData;
                    return data?.Target.Id;
                })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .Where(id => currentlyBannedIds.Contains(id))
                .ToList();

            if (userIds.Count == 0)
            {
                await command.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = "✅ No currently banned users found from the last 12 hours.";
                });
                return;
            }

            int success = 0;
            int failed = 0;

            foreach (var userId in userIds)
            {
                try
                {
                    await guild.RemoveBanAsync(userId);
                    success++;
                    await Task.Delay(500);
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
                    $"✅ Finished unbanning users from the past 12 hours.\n\n" +
                    $"Found current bans: **{userIds.Count}**\n" +
                    $"Unbanned: **{success}**\n" +
                    $"Failed: **{failed}**";
            });
        }
        catch (Exception ex)
        {
            await command.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = $"❌ Error while checking bans: {ex.Message}";
            });
        }
    }
}