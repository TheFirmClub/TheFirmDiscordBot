using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class CheckRoleInfoCommand
{
    private static readonly HashSet<ulong> AllowedUserRoleIds = new()
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

    private const int PageSize = 10;

    // Tracks active paginations: key = userId + roleId
    private readonly Dictionary<string, RolePaginationState> _paginationStates = new();

    private class RolePaginationState
    {
        public SocketRole? Role;
        public List<SocketGuildUser>? Members;
        public int Page;
        public ulong ChannelId;
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (user == null || !user.Roles.Any(r => AllowedUserRoleIds.Contains(r.Id)))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (!(command.Channel is SocketGuildChannel guildChannel))
        {
            await command.RespondAsync("This command must be used in a guild.", ephemeral: true);
            return;
        }

        var guild = guildChannel.Guild;

        // Get input (role ID or role name)
        var roleInput = (string?)command.Data.Options.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(roleInput))
        {
            await RespondAllowedRoles(command, guild);
            return;
        }

        SocketRole? role = null;

        // Try parse as ID
        if (ulong.TryParse(roleInput, out ulong roleId))
            role = guild.GetRole(roleId);

        // If not found, try match by name (case-insensitive)
        if (role == null)
            role = guild.Roles.FirstOrDefault(r => string.Equals(r.Name, roleInput, StringComparison.OrdinalIgnoreCase));

        if (role == null)
        {
            // Role not found, show breakdown of allowed roles
            await RespondAllowedRoles(command, guild);
            return;
        }

        var members = role.Members.OrderBy(m => m.DisplayName).ToList();
        if (!members.Any())
        {
            await command.RespondAsync("No members have this role.", ephemeral: true);
            return;
        }

        // Store pagination state
        string key = $"{user.Id}_{role.Id}";
        _paginationStates[key] = new RolePaginationState
        {
            Role = role,
            Members = members,
            Page = 0,
            ChannelId = command.Channel.Id
        };

        await SendRolePage(command, _paginationStates[key]);
    }

    private async Task RespondAllowedRoles(SocketSlashCommand command, SocketGuild guild)
    {
        var allowedRoles = guild.Roles
            .Where(r => AllowedUserRoleIds.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => $"{r.Name} (`{r.Id}`)")
            .ToList();

        var embed = new EmbedBuilder()
            .WithTitle("Allowed Roles")
            .WithDescription(allowedRoles.Any()
                ? string.Join("\n", allowedRoles)
                : "No roles available.")
            .WithColor(Color.Orange)
            .Build();

        await command.RespondAsync("❗ Role not found. Here's a list of roles you can check:", embed: embed, ephemeral: true);
    }

    public async Task HandleButton(SocketMessageComponent component)
    {
        var userId = component.User.Id;

        var parts = component.Data.CustomId.Split('_'); // roleinfo_{roleId}_{next/prev}
        if (parts.Length != 3 || !ulong.TryParse(parts[1], out ulong roleId))
        {
            await component.RespondAsync("Invalid button interaction.", ephemeral: true);
            return;
        }

        string key = $"{userId}_{roleId}";
        if (!_paginationStates.TryGetValue(key, out var state))
        {
            await component.RespondAsync("❌ Session expired. Use /checkroleinfo again.", ephemeral: true);
            return;
        }

        var direction = parts[2];
        int totalPages = (state.Members!.Count + PageSize - 1) / PageSize;

        if (direction == "next")
            state.Page = Math.Min(state.Page + 1, totalPages - 1);
        else if (direction == "prev")
            state.Page = Math.Max(state.Page - 1, 0);

        await SendRolePage(component, state);
    }

    private async Task SendRolePage(SocketInteraction interaction, RolePaginationState state)
    {
        int totalPages = (state.Members!.Count + PageSize - 1) / PageSize;
        var pageMembers = state.Members.Skip(state.Page * PageSize).Take(PageSize);

        var embed = new EmbedBuilder()
            .WithTitle($"Role Info: {state.Role!.Name}")
            .WithDescription(string.Join("\n", pageMembers.Select(m => $"{m.Mention} ({m.Id})")))
            .WithFooter($"Page {state.Page + 1}/{totalPages}")
            .WithColor(Color.Green)
            .Build();

        var componentBuilder = new ComponentBuilder()
            .WithButton("Previous", $"roleinfo_{state.Role!.Id}_prev", disabled: state.Page == 0)
            .WithButton("Next", $"roleinfo_{state.Role!.Id}_next", disabled: state.Page == totalPages - 1);

        switch (interaction)
        {
            case SocketSlashCommand slash:
                await slash.RespondAsync(embed: embed, components: componentBuilder.Build(), ephemeral: true);
                break;

            case SocketMessageComponent comp:
                await comp.UpdateAsync(msg =>
                {
                    msg.Embed = embed;
                    msg.Components = componentBuilder.Build();
                });
                break;
        }
    }

    // 🔍 Autocomplete for /checkroleinfo role option
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        // Must be in a guild
        if (interaction.Channel is not SocketGuildChannel guildChannel)
            return;

        var guild = guildChannel.Guild;

        // What the user has typed so far
        var currentValue = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        // Match roles that START WITH what they typed (e.g. "Senior")
        var results = guild.Roles
            .Where(r => !r.IsEveryone)
            .Where(r => r.Name.StartsWith(currentValue, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name)
            .Take(25) // Discord limit
            .Select(r => new AutocompleteResult(
                name: r.Name,
                value: r.Id.ToString()
            ))
            .ToList();

        await interaction.RespondAsync(results);
    }

}
