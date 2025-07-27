using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class TicketService
{
    private readonly DiscordSocketClient _client;
    private readonly ulong[] _allowedRolesToOpenTicket = { /* add your allowed role IDs here */ 1393589436402634874 };
    private readonly ulong _supportRoleId = 1393590761953558608; // Your Senior Management / Support role ID

    // Map ticket reasons to category channel IDs in your server
    private readonly Dictionary<string, ulong> _ticketCategories = new()
    {
        { "ban", 1393628885484044299 },     // Replace with your Ban Reports category ID
        { "support", 1393610364511326259 }, // Replace with your Support category ID
        { "subscription", 1393627644326838292 }  // Replace with your subscription category ID
    };

    public TicketService(DiscordSocketClient client)
    {
        _client = client;
        _client.InteractionCreated += InteractionCreatedAsync;
    }

    // Call this method to send the ticket panel embed + select menu
    public async Task SendTicketPanelAsync(ISocketMessageChannel channel)
    {
        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("ticket_select_reason")
            .WithPlaceholder("Select the reason for your ticket...")
            .AddOption("🐞 Ban Dispute Ticket", "ban", "Open a Ban Dispute Ticket")
            .AddOption("💬 General Support", "support", "Get help from support")
            .AddOption("💰 Subscription", "subscription", "Subscription Support");

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        var embed = new EmbedBuilder()
            .WithTitle("🎫 Open a Ticket")
            .WithDescription("Select a reason from the dropdown below to open a ticket.")
            .WithColor(Color.Blue)
            .Build();

        await channel.SendMessageAsync(embed: embed, components: component);
    }

    private async Task InteractionCreatedAsync(SocketInteraction interaction)
    {
        if (interaction is SocketMessageComponent component)
        {
            if (component.Data.CustomId == "ticket_select_reason")
            {
                var user = component.User as SocketGuildUser;
                if (user == null)
                {
                    await component.RespondAsync("Error: Could not identify user.", ephemeral: true);
                    return;
                }

                // Check allowed roles
                if (!_allowedRolesToOpenTicket.Any(roleId => user.Roles.Any(r => r.Id == roleId)))
                {
                    await component.RespondAsync("❌ You do not have permission to open tickets.", ephemeral: true);
                    return;
                }

                string reason = component.Data.Values.First();
                await CreateTicketChannelAsync(component, user, reason);
            }
        }
        else if (interaction is SocketSlashCommand slashCommand)
        {
            if (slashCommand.CommandName == "close")
            {
                await CloseTicketAsync(slashCommand);
            }
        }
    }

    private async Task CreateTicketChannelAsync(SocketMessageComponent component, SocketGuildUser user, string reason)
    {
        var guild = user.Guild;
        string channelName = $"ticket-{user.Username.ToLower()}-{reason}";

        // Check for existing ticket channel by name
        var existing = guild.TextChannels.FirstOrDefault(c => c.Name == channelName);
        if (existing != null)
        {
            await component.RespondAsync($"You already have an open ticket: {existing.Mention}", ephemeral: true);
            return;
        }

        if (!_ticketCategories.TryGetValue(reason, out ulong categoryId))
        {
            await component.RespondAsync("❌ Invalid ticket reason.", ephemeral: true);
            return;
        }

        var categoryChannel = guild.GetCategoryChannel(categoryId);
        if (categoryChannel == null)
        {
            await component.RespondAsync("❌ Ticket category not found. Contact admin.", ephemeral: true);
            return;
        }

        var supportRole = guild.GetRole(_supportRoleId);

        var overwrites = new List<Overwrite>
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
        };

        if (supportRole != null)
        {
            overwrites.Add(new Overwrite(supportRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)));
        }

        var channel = await guild.CreateTextChannelAsync(channelName, c =>
        {
            c.CategoryId = categoryChannel.Id;
            c.PermissionOverwrites = overwrites;
            c.Topic = $"Ticket for {user.Username} - Reason: {reason}";
        });

        await component.RespondAsync($"✅ Ticket created: {channel.Mention}", ephemeral: true);

        var embed = new EmbedBuilder()
            .WithTitle($"Ticket: {reason.ToUpper()}")
            .WithDescription($"Hello {user.Mention}, a support member will be with you shortly.\nUse `/close` to close this ticket.")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);

        if (supportRole != null)
        {
            await channel.SendMessageAsync($"{supportRole.Mention} New ticket opened by {user.Mention} for **{reason}**.");
        }
    }

    public async Task CloseTicketAsync(SocketSlashCommand command)
    {
        var channel = command.Channel as SocketTextChannel;
        var user = command.User as SocketGuildUser;

        if (channel == null)
        {
            await command.RespondAsync("This command can only be used in a server text channel.", ephemeral: true);
            return;
        }

        if (!channel.Name.StartsWith("ticket-"))
        {
            await command.RespondAsync("This is not a ticket channel.", ephemeral: true);
            return;
        }

        // Defer the response immediately to avoid timeout
        await command.DeferAsync(ephemeral: true);

        // Wait a little before deleting the channel
        await Task.Delay(3000);

        await channel.DeleteAsync();
    }
}
