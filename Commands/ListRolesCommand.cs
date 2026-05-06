using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public class ListRolesCommand : ISlashCommand
{
    public string Name => "listroles";
    public string Description => "Lists all server roles with IDs";

    private const int RolesPerPage = 20;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller || !caller.GuildPermissions.ManageRoles)
        {
            await command.RespondAsync("❌ You need the **Manage Roles** permission to use this command.", ephemeral: true);
            return;
        }

        var guild = caller.Guild;

        var roles = guild.Roles
            .Where(r => !r.IsEveryone)
            .OrderByDescending(r => r.Position)
            .ToList();

        if (!roles.Any())
        {
            await command.RespondAsync("No roles found.", ephemeral: true);
            return;
        }

        var embed = BuildEmbed(guild, roles, 0);

        var buttons = BuildButtons(0, GetTotalPages(roles.Count));

        await command.RespondAsync(embed: embed, components: buttons, ephemeral: true);
    }

    public static async Task HandleButton(SocketMessageComponent component)
    {
        if (component.User is not SocketGuildUser caller || !caller.GuildPermissions.ManageRoles)
        {
            await component.RespondAsync("❌ You need the **Manage Roles** permission to use this.", ephemeral: true);
            return;
        }

        var guild = caller.Guild;

        var roles = guild.Roles
            .Where(r => !r.IsEveryone)
            .OrderByDescending(r => r.Position)
            .ToList();

        var parts = component.Data.CustomId.Split("_");

        if (parts.Length < 3 || !int.TryParse(parts[2], out var page))
        {
            await component.RespondAsync("❌ Invalid page button.", ephemeral: true);
            return;
        }

        var totalPages = GetTotalPages(roles.Count);

        if (component.Data.CustomId.StartsWith("listroles_prev_"))
            page--;

        if (component.Data.CustomId.StartsWith("listroles_next_"))
            page++;

        page = Math.Clamp(page, 0, totalPages - 1);

        var embed = BuildEmbed(guild, roles, page);
        var buttons = BuildButtons(page, totalPages);

        await component.UpdateAsync(msg =>
        {
            msg.Embed = embed;
            msg.Components = buttons;
        });
    }

    private static Embed BuildEmbed(SocketGuild guild, List<SocketRole> roles, int page)
    {
        var totalPages = GetTotalPages(roles.Count);

        var pageRoles = roles
            .Skip(page * RolesPerPage)
            .Take(RolesPerPage);

        var sb = new StringBuilder();

        foreach (var role in pageRoles)
        {
            sb.AppendLine($"**{role.Name}**");
            sb.AppendLine($"`{role.Id}`");
            sb.AppendLine();
        }

        return new EmbedBuilder()
            .WithTitle($"📜 Roles in {guild.Name}")
            .WithDescription(sb.ToString())
            .WithColor(Color.Blue)
            .WithFooter($"Page {page + 1}/{totalPages} • Total Roles: {roles.Count}")
            .WithCurrentTimestamp()
            .Build();
    }

    private static MessageComponent BuildButtons(int page, int totalPages)
    {
        return new ComponentBuilder()
            .WithButton("⬅️ Previous", $"listroles_prev_{page}", ButtonStyle.Secondary, disabled: page <= 0)
            .WithButton("➡️ Next", $"listroles_next_{page}", ButtonStyle.Secondary, disabled: page >= totalPages - 1)
            .Build();
    }

    private static int GetTotalPages(int count)
    {
        return (int)Math.Ceiling(count / (double)RolesPerPage);
    }
}