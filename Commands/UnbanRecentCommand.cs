using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord.Rest;

public class UnbanRecentCommand : ISlashCommand
{
    public string Name => "unbanrecent";
    public string Description => "Unban everyone banned within the past 12 hours.";

    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393590761953558608 // SM role
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

        var guild = guildUser.Guild;
        var since = DateTimeOffset.UtcNow.AddHours(-12);

        await command.RespondAsync("⏳ Checking bans from the past 12 hours...", ephemeral: true);

        try
        {
            var auditLogs = await guild.GetAuditLogsAsync(
                limit: 100,
                actionType: ActionType.Ban
            ).FlattenAsync();

            var recentBans = auditLogs
                .Where(log => log.CreatedAt >= since)
                .Where(log => log.Data is BanAuditLogData)
                .Select(log => ((BanAuditLogData)log.Data).Target.Id)
                .Distinct()
                .ToList();

            if (recentBans.Count == 0)
            {
                await command.ModifyOriginalResponseAsync(msg =>
                    msg.Content = "✅ No bans found from the past 12 hours."
                );
                return;
            }

            int success = 0;
            int failed = 0;

            foreach (var userId in recentBans)
            {
                try
                {
                    await guild.RemoveBanAsync(userId);
                    success++;
                }
                catch
                {
                    failed++;
                }
            }

            await command.ModifyOriginalResponseAsync(msg =>
                msg.Content = $"✅ Finished unbanning users from the past 12 hours.\n\nUnbanned: **{success}**\nFailed: **{failed}**"
            );
        }
        catch (Exception ex)
        {
            await command.ModifyOriginalResponseAsync(msg =>
                msg.Content = $"❌ Failed to check/unban recent bans.\nError: {ex.Message}"
            );
        }
    }
}