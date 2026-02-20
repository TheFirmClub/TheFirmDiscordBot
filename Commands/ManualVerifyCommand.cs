using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FirmDiscordBot.Commands;

public sealed class ManualVerifyCommand : ISlashCommand
{
    public string Name => "manualverify";
    public string Description => "Verify a user (fallback) by Discord ID.";

    private const ulong VerifiedRoleId = 1393625125257089135;
    private const ulong ManualVerifyRoleId = 1474419863798812752;

    private static readonly ulong[] StaffRoleIds =
    {
        1393729574537396355,
        1393623589122736238,
        1393638449709584434,
        1405330877440983130,
        1393728468608487594,
        1393590761953558608
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser invoker)
        {
            await command.RespondAsync("❌ Could not resolve your guild user.", ephemeral: true);
            return;
        }

        if (!invoker.Roles.Any(r => StaffRoleIds.Contains(r.Id)))
        {
            await command.RespondAsync("⛔ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        var guild = invoker.Guild;

        var idStr = command.Data.Options.FirstOrDefault(o => o.Name == "discordid")?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(idStr) || !ulong.TryParse(idStr, out var targetId))
        {
            await command.RespondAsync("❌ Invalid Discord ID. Example: `/manualverify 123456789012345678`", ephemeral: true);
            return;
        }

        // ✅ IMPORTANT: use cached SocketGuildUser so Roles works and no GetUserAsync needed
        var target = guild.GetUser(targetId);
        if (target == null)
        {
            await command.RespondAsync("❌ Could not find that user in this server (they must be in the guild).", ephemeral: true);
            return;
        }

        var verifiedRole = guild.GetRole(VerifiedRoleId);
        if (verifiedRole == null)
        {
            await command.RespondAsync("❌ Verified role not found.", ephemeral: true);
            return;
        }

        await target.AddRoleAsync(verifiedRole);

        var manualRole = guild.GetRole(ManualVerifyRoleId);
        if (manualRole != null && target.Roles.Any(r => r.Id == ManualVerifyRoleId))
            await target.RemoveRoleAsync(manualRole);

        await command.RespondAsync($"✅ {target.Mention} has been given **Verified** (Manual Verify removed).", ephemeral: true);

        if (command.Channel is SocketTextChannel ch)
            await ch.SendMessageAsync($"✅ {target.Mention} verified by {command.User.Mention}.");
    }
}