using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class TicketService
{
    private readonly DiscordSocketClient _client;

    // CONFIG
    private readonly ulong[] _allowedRolesToOpenTicket = { 1393589436402634874 };
    private readonly ulong _supportRoleId = 1393590761953558608;
    private readonly ulong _transcriptsChannelId = 1393655041163456512; // Replace with your transcripts channel ID

    private readonly Dictionary<string, ulong> _ticketCategories = new()
    {
        { "ban", 1393628885484044299 },
        { "support", 1393610364511326259 },
        { "subscription", 1393627644326838292 }
    };

    public TicketService(DiscordSocketClient client)
    {
        _client = client;
        _client.InteractionCreated += InteractionCreatedAsync;
    }

    public async Task SendTicketPanelAsync(ISocketMessageChannel channel)
    {
        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("ticket_select_reason")
            .WithPlaceholder("Welcome to our tickets channel!")
            .AddOption("🐞 Ban Dispute Ticket", "ban", "Open a Ban Dispute Ticket")
            .AddOption("💬 General Support", "support", "Get help from support")
            .AddOption("💰 Subscription", "subscription", "Subscription Support");

        var component = new ComponentBuilder().WithSelectMenu(selectMenu).Build();

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
            switch (component.Data.CustomId)
            {
                case "ticket_select_reason":
                    var user = component.User as SocketGuildUser;
                    if (user == null || !_allowedRolesToOpenTicket.Any(roleId => user.Roles.Any(r => r.Id == roleId)))
                    {
                        await component.RespondAsync("❌ You do not have permission to open tickets.", ephemeral: true);
                        return;
                    }

                    string reason = component.Data.Values.First();
                    await CreateTicketChannelAsync(component, user, reason);
                    break;

                case "ticket_button_complete":
                    await HandleCompleteButtonAsync(component);
                    break;

                case "resolve_yes":
                    await HandleResolveYesAsync(component);
                    break;

                case "resolve_no":
                    await component.RespondAsync("Thanks for the feedback! Please provide more information and staff will assist you shortly.", ephemeral: true);
                    break;
            }
        }
        else if (interaction is SocketSlashCommand command)
        {
            switch (command.CommandName)
            {
                case "close":
                    await CloseTicketAsync(command);
                    break;
                case "ticketaddrole":
                    await TicketAddRoleAsync(command);
                    break;
                case "ticketadduser":
                    await TicketAddUserAsync(command);
                    break;
                case "ticketclaim":
                    await ClaimTicketAsync(command);
                    break;
                case "ticketrelease":
                    await ReleaseTicketAsync(command);
                    break;
                case "ticketresolve":
                    await ResolveTicketAsync(command);
                    break;
                case "ticketrestrict":
                    await RestrictTicketAsync(command);
                    break;
                case "tempticket":
                    await CreateTempTicketAsync(command);
                    break;
            }
        }
    }

    private async Task CreateTicketChannelAsync(SocketMessageComponent component, SocketGuildUser user, string reason)
    {
        var guild = user.Guild;
        string safeUsername = new string(user.Username.Where(char.IsLetterOrDigit).ToArray()).ToLower();
        string channelName = $"ticket-{safeUsername}-{reason}";

        if (guild.TextChannels.Any(c => c.Name == channelName))
        {
            await component.RespondAsync("You already have an open ticket.", ephemeral: false);
            return;
        }

        if (!_ticketCategories.TryGetValue(reason, out ulong categoryId))
        {
            await component.RespondAsync("❌ Invalid ticket reason.", ephemeral: false);
            return;
        }

        var categoryChannel = guild.GetCategoryChannel(categoryId);
        var supportRole = guild.GetRole(_supportRoleId);

        var overwrites = new List<Overwrite>
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
        };

        if (supportRole != null)
            overwrites.Add(new Overwrite(supportRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));

        var channel = await guild.CreateTextChannelAsync(channelName, c =>
        {
            c.CategoryId = categoryChannel.Id;
            c.PermissionOverwrites = overwrites;
            c.Topic = $"Ticket for {user.Username} - Reason: {reason}";
        });

        await component.RespondAsync($"✅ Ticket created: {channel.Mention}", ephemeral: false);

        var embed = new EmbedBuilder()
            .WithTitle($"Ticket: {reason.ToUpper()}")
            .WithDescription($"Hello {user.Mention}, a support member will be with you shortly.\nUse `/close` to close this ticket.")
            .WithColor(Color.Green)
            .Build();

        await channel.SendMessageAsync(embed: embed);

        if (supportRole != null)
        {
            await channel.SendMessageAsync($"{supportRole.Mention} New ticket opened by {user.Mention} for **{reason}**.");
        }
    }

    public async Task CloseTicketAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true); // 🔁 MUST be at the top, within 3 sec
        var channel = command.Channel as SocketTextChannel;

        if (channel == null || !channel.Name.StartsWith("ticket-"))
        {
            await command.RespondAsync("This command must be used in a ticket channel.", ephemeral: false);
            return;
        }

        var builder = new ComponentBuilder()
            .WithButton("Complete & Delete Ticket", "ticket_button_complete", ButtonStyle.Danger);

        await command.RespondAsync("Ticket marked for closure. Click below to finalize:", components: builder.Build(), ephemeral: false);
    }

    private async Task TicketAddRoleAsync(SocketSlashCommand command)
    {
        var role = command.Data.Options.FirstOrDefault(o => o.Name == "role")?.Value as SocketRole;
        var channel = command.Channel as SocketTextChannel;

        if (channel == null || role == null)
        {
            await command.RespondAsync("Invalid channel or role.", ephemeral: false);
            return;
        }

        await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        await command.RespondAsync($"Added {role.Mention} to this ticket.", ephemeral: false);
    }

    private async Task TicketAddUserAsync(SocketSlashCommand command)
    {
        var user = command.Data.Options.FirstOrDefault(o => o.Name == "user")?.Value as SocketGuildUser;
        var channel = command.Channel as SocketTextChannel;

        if (channel == null || user == null)
        {
            await command.RespondAsync("Invalid channel or user.", ephemeral: false);
            return;
        }

        await channel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        await command.RespondAsync($"Added {user.Mention} to this ticket.", ephemeral: false);
    }

    private async Task ClaimTicketAsync(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        var channel = command.Channel as SocketTextChannel;

        if (channel == null || user == null)
        {
            await command.RespondAsync("Error: Cannot claim ticket.", ephemeral: false);
            return;
        }

        await channel.ModifyAsync(p => p.Topic = $"Claimed by {user.Username}#{user.Discriminator}");
        await command.RespondAsync($"{user.Mention} has claimed this ticket.", ephemeral: false);
    }

    private async Task ReleaseTicketAsync(SocketSlashCommand command)
    {
        var channel = command.Channel as SocketTextChannel;
        if (channel == null)
        {
            await command.RespondAsync("Invalid channel.", ephemeral: false);
            return;
        }

        await channel.ModifyAsync(p => p.Topic = null);
        await command.RespondAsync("Ticket released.", ephemeral: false);
    }

    private async Task ResolveTicketAsync(SocketSlashCommand command)
    {
        var channel = command.Channel as SocketTextChannel;
        var guild = channel?.Guild;
        var user = command.User as SocketGuildUser;

        if (channel == null || guild == null || user == null)
        {
            await command.RespondAsync("Invalid context for resolve command.", ephemeral: false);
            return;
        }

        var ticketOwnerId = channel.PermissionOverwrites
            .Where(o => o.TargetType == PermissionTarget.User)
            .Select(o => o.TargetId)
            .FirstOrDefault(id => id != user.Id && id != guild.EveryoneRole.Id);

        if (ticketOwnerId == 0)
        {
            await command.RespondAsync("Could not determine ticket owner.", ephemeral: false);
            return;
        }

        var ticketOwner = guild.GetUser(ticketOwnerId);

        if (ticketOwner == null)
        {
            await command.RespondAsync("Ticket owner not found.", ephemeral: false);
            return;
        }

        var dm = await ticketOwner.CreateDMChannelAsync();
        var builder = new ComponentBuilder()
            .WithButton("Accept Resolution", "resolve_yes", ButtonStyle.Success)
            .WithButton("Reject Resolution", "resolve_no", ButtonStyle.Danger);

        await dm.SendMessageAsync($"Your ticket in #{channel.Name} was resolved. Do you accept?", components: builder.Build());
        await command.RespondAsync("Resolution prompt sent.", ephemeral: false);
    }

    private async Task RestrictTicketAsync(SocketSlashCommand command)
    {
        var channel = command.Channel as SocketTextChannel;
        var role = command.Data.Options.FirstOrDefault(o => o.Name == "role")?.Value as SocketRole;

        if (channel == null || role == null)
        {
            await command.RespondAsync("Invalid role or channel.", ephemeral: false);
            return;
        }

        // Remove all other role overwrites except this one
        foreach (var overwrite in channel.PermissionOverwrites)
        {
            if (overwrite.TargetType == PermissionTarget.Role && overwrite.TargetId != role.Id)
            {
                var existingRole = channel.Guild.GetRole(overwrite.TargetId);
                if (existingRole != null)
                    await channel.RemovePermissionOverwriteAsync(existingRole);
            }
        }

        await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
        await command.RespondAsync($"Ticket now restricted to {role.Mention}.", ephemeral: false);
    }

    private async Task CreateTempTicketAsync(SocketSlashCommand command)
    {
        var user = command.Data.Options.FirstOrDefault(o => o.Name == "user")?.Value as SocketGuildUser;
        if (user == null)
        {
            await command.RespondAsync("Invalid user.", ephemeral: false);
            return;
        }

        // The method CreateTicketChannelAsync expects a SocketMessageComponent; we need a workaround.
        // Instead, create ticket directly here:

        var guild = user.Guild;
        string reason = "support";
        string safeUsername = new string(user.Username.Where(char.IsLetterOrDigit).ToArray()).ToLower();
        string channelName = $"ticket-{safeUsername}-{reason}";

        if (guild.TextChannels.Any(c => c.Name == channelName))
        {
            await command.RespondAsync("User already has an open ticket.", ephemeral: false);
            return;
        }

        if (!_ticketCategories.TryGetValue(reason, out ulong categoryId))
        {
            await command.RespondAsync("❌ Invalid ticket reason.", ephemeral: false);
            return;
        }

        var categoryChannel = guild.GetCategoryChannel(categoryId);
        var supportRole = guild.GetRole(_supportRoleId);

        var overwrites = new List<Overwrite>
        {
            new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
        };

        if (supportRole != null)
            overwrites.Add(new Overwrite(supportRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)));

        var channel = await guild.CreateTextChannelAsync(channelName, c =>
        {
            c.CategoryId = categoryChannel.Id;
            c.PermissionOverwrites = overwrites;
            c.Topic = $"Ticket for {user.Username} - Reason: {reason}";
        });

        var embed = new EmbedBuilder()
            .WithTitle($"Ticket: {reason.ToUpper()}")
            .WithDescription($"Hello {user.Mention}, a support member will be with you shortly.\nUse `/close` to close this ticket.")
            .WithColor(Color.Green)
            .Build();

        await channel.SendMessageAsync(embed: embed);

        if (supportRole != null)
        {
            await channel.SendMessageAsync($"{supportRole.Mention} New ticket opened for **{user.Mention}**.");
        }

        await command.RespondAsync($"✅ Temporary ticket created: {channel.Mention}", ephemeral: false);
    }

    public async Task HandleCompleteButtonAsync(SocketMessageComponent component)
    {
        var channel = component.Channel as SocketTextChannel;
        if (channel == null)
        {
            await component.RespondAsync("This is not a ticket channel.", ephemeral: false);
            return;
        }

        var guild = channel.Guild;
        var messages = await channel.GetMessagesAsync(100).FlattenAsync();

        var sb = new StringBuilder();
        foreach (var msg in messages.OrderBy(m => m.Timestamp))
        {
            // For embeds or attachments, you might want to handle differently
            sb.AppendLine($"[{msg.Timestamp.UtcDateTime}] {msg.Author.Username}: {msg.Content}");
        }

        var transcript = sb.ToString();
        var transcriptChannel = guild.GetTextChannel(_transcriptsChannelId);

        // Find ticket owner by permission overwrite for User (not Everyone)
        var overwrite = channel.PermissionOverwrites
            .FirstOrDefault(p => p.TargetType == PermissionTarget.User && p.TargetId != guild.EveryoneRole.Id);

        ulong ticketOwnerId = overwrite.TargetId;

        var ticketOwner = guild.GetUser(ticketOwnerId);
        if (ticketOwner != null)
        {
            try
            {
                var dm = await ticketOwner.CreateDMChannelAsync();
                await dm.SendMessageAsync($"Transcript for ticket **{channel.Name}**:\n```\n{transcript}\n```");
            }
            catch
            {
                // Ignore if can't DM user
            }
        }

        if (transcriptChannel != null)
        {
            await transcriptChannel.SendMessageAsync($"Transcript for **{channel.Name}**:\n```\n{transcript}\n```");
        }

        await channel.DeleteAsync();
    }

    private async Task HandleResolveYesAsync(SocketMessageComponent component)
    {
        var channel = component.Channel as SocketTextChannel;
        var user = component.User as SocketGuildUser;

        if (channel == null || !channel.Name.StartsWith("ticket-"))
        {
            await component.RespondAsync("❌ This is not a valid ticket channel.", ephemeral: false);
            return;
        }

        // Disable buttons on original message to prevent multiple clicks
        if (component.Message is IUserMessage msg)
        {
            var builder = new ComponentBuilder();

            foreach (var actionRow in component.Message.Components)
            {
                var rowBuilder = new ActionRowBuilder();

                foreach (var comp in actionRow.Components)
                {
                    if (comp is ButtonComponent button)
                    {
                        var disabledButton = new ButtonBuilder()
                            .WithStyle(button.Style)
                            .WithCustomId(button.CustomId)
                            .WithLabel(button.Label)
                            .WithDisabled(true);

                        rowBuilder.AddComponent(disabledButton.Build());
                    }
                    else
                    {
                        rowBuilder.AddComponent(comp);
                    }
                }


                builder.AddRow(rowBuilder);
            }

            await msg.ModifyAsync(m => m.Components = builder.Build());
        }

        var mention = user?.Mention ?? "Someone";

        var embed = new EmbedBuilder()
            .WithTitle("Ticket Resolved")
            .WithDescription($"{mention} has marked this ticket as resolved. Staff will close the ticket shortly.")
            .WithColor(Color.Green)
            .Build();

        await component.Channel.SendMessageAsync(embed: embed);
        await component.RespondAsync("You have marked this ticket as resolved.", ephemeral: false);
    }
}
