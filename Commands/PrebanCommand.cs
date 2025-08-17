using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class PrebanCommand : ISlashCommand
{
    public string Name => "preban";
    public string Description => "Ban someone from the server before they even join (requires user ID).";

    // ✅ Allowed role(s) for using this command
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
            .AddOption("userid", ApplicationCommandOptionType.String, "The Discord user ID to ban", isRequired: true)
            .AddOption("reason", ApplicationCommandOptionType.String, "Reason for the ban", isRequired: false)
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser guildUser)
        {
            await command.RespondAsync("❌ Command can only be used in a guild.", ephemeral: true);
            return;
        }

        // 🚫 Role check
        if (!HasPermission(guildUser))
        {
            await command.RespondAsync("❌ You don’t have permission to use this command.", ephemeral: true);
            return;
        }

        string userIdStr = (string)command.Data.Options.First(o => o.Name == "userid").Value;
        string reason = command.Data.Options.FirstOrDefault(o => o.Name == "reason")?.Value?.ToString() ?? "No reason provided";

        if (!ulong.TryParse(userIdStr, out ulong userId))
        {
            await command.RespondAsync("❌ Invalid user ID format.", ephemeral: true);
            return;
        }

        var guild = guildUser.Guild;
        try
        {
            await guild.AddBanAsync(userId, 0, reason);
            await command.RespondAsync($"✅ Successfully pre-banned user **{userId}**.\nReason: {reason}");
        }
        catch (Exception ex)
        {
            await command.RespondAsync($"❌ Failed to ban user. Error: {ex.Message}", ephemeral: true);
        }
    }
}
