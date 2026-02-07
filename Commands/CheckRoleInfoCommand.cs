using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CheckRoleInfoCommand : ISlashCommand
{
    public string Name => "checkroleinfo";
    public string Description => "View information about a role";

    // 🔒 Allowed roles
    private static readonly HashSet<ulong> AllowedRoleIds = new()
    {
        1420512528395665569, // MET Command
        1420513009729802260, // Civil Command
        1420512797191704616, // NHS Command
        1393623589122736238, // Discord Moderator
        1393638449709584434, // Senior Moderator
        1393590761953558608, // Senior Management
        1399173940622135448, // Section Leadership
        1463090510406225991, // Operational Command
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (command.User is not SocketGuildUser user ||
            !user.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await command.ModifyOriginalResponseAsync(m =>
                m.Content = "❌ You are not authorised to use this command.");
            return;
        }

        var role = command.Data.Options.First().Value as SocketRole;
        if (role == null)
        {
            await command.ModifyOriginalResponseAsync(m =>
                m.Content = "❌ Invalid role.");
            return;
        }

        var members = role.Guild.Users
            .Where(u => u.Roles.Any(r => r.Id == role.Id))
            .OrderBy(u => u.DisplayName)
            .ToList();

        await SendPage(command, role, members, 0);
    }

    // =============================
    // PAGINATION + EMBED RENDERING
    // =============================
    private static async Task SendPage(
        SocketSlashCommand command,
        SocketRole role,
        List<SocketGuildUser> members,
        int page
    )
    {
        const int PageSize = 10;

        int totalPages = Math.Max(1,
            (int)Math.Ceiling(members.Count / (double)PageSize));

        page = Math.Clamp(page, 0, totalPages - 1);

        var pageMembers = members
            .Skip(page * PageSize)
            .Take(PageSize)
            .Select(m => m.Mention);

        var embed = new EmbedBuilder()
            .WithTitle("Role Info")
            .AddField("Name", role.Name, true)
            .AddField("Members", members.Count, true)
            .AddField("Color", $"#{role.Color.RawValue:X6}", true)
            .AddField("Role ID", role.Id, true)
            .AddField("Member Names",
                pageMembers.Any() ? string.Join("\n", pageMembers) : "*No members*")
            .WithFooter($"Page {page + 1} / {totalPages}")
            .WithColor(role.Color)
            .Build();

        var components = new ComponentBuilder()
            .WithButton(
                "⬅️ Prev",
                $"roleinfo_prev:{role.Id}:{page}",
                ButtonStyle.Secondary,
                disabled: page == 0)
            .WithButton(
                "Next ➡️",
                $"roleinfo_next:{role.Id}:{page}",
                ButtonStyle.Secondary,
                disabled: page >= totalPages - 1)
            .Build();

        await command.ModifyOriginalResponseAsync(m =>
        {
            m.Embed = embed;
            m.Components = components;
        });
    }

    // =============================
    // BUTTON HANDLER
    // =============================
    public static async Task HandleButtonAsync(SocketMessageComponent component)
    {
        if (component.User is not SocketGuildUser user ||
            !user.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await component.RespondAsync("❌ Not authorised.", ephemeral: true);
            return;
        }

        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3) return;

        bool next = parts[0] == "roleinfo_next";
        ulong roleId = ulong.Parse(parts[1]);
        int page = int.Parse(parts[2]);

        var role = component.Guild.GetRole(roleId);
        if (role == null) return;

        var members = component.Guild.Users
            .Where(u => u.Roles.Any(r => r.Id == role.Id))
            .OrderBy(u => u.DisplayName)
            .ToList();

        int newPage = next ? page + 1 : page - 1;

        await SendPage(
            component.Message.Interaction as SocketSlashCommand,
            role,
            members,
            newPage
        );

        await component.DeferAsync();
    }
}
