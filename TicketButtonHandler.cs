using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class TicketButtonHandler
{
    private readonly ulong[] _moderatorRoleIds = new ulong[]
    {
        1393729574537396355,
        1393623589122736238
    };

    public async Task HandleAsync(SocketMessageComponent component)
    {
        var user = component.User as SocketGuildUser;
        bool isMod = user.Roles.Any(r => _moderatorRoleIds.Contains(r.Id));

        if (!isMod)
        {
            await component.RespondAsync("❌ Only moderators can use this.", ephemeral: true);
            return;
        }

        var originalMessage = component.Message;

        // Copy existing embed (if present)
        var originalEmbed = originalMessage.Embeds.FirstOrDefault();
        var embedBuilder = new EmbedBuilder();
        if (originalEmbed != null)
        {
            embedBuilder.WithTitle(originalEmbed.Title)
                        .WithDescription(originalEmbed.Description)
                        .WithColor(originalEmbed.Color.GetValueOrDefault(Color.Orange))
                        .WithTimestamp(originalEmbed.Timestamp ?? DateTimeOffset.UtcNow)
                        .WithFooter(originalEmbed.Footer?.Text, originalEmbed.Footer?.IconUrl);

            foreach (var field in originalEmbed.Fields)
                embedBuilder.AddField(field.Name, field.Value, field.Inline);
        }

        switch (component.Data.CustomId)
        {
            case "ticket_claim":
                if (originalMessage.Components.First().Components.FirstOrDefault(b => b.CustomId == "ticket_claim") is ButtonComponent claimBtn && claimBtn.IsDisabled)
                {
                    await component.RespondAsync("⚠️ This ticket has already been claimed.", ephemeral: true);
                    return;
                }

                embedBuilder.AddField("👮 Claimed By", user.Mention, true);

                var claimButtons = new ComponentBuilder()
                    .WithButton("🎯 Claimed", "ticket_claim", ButtonStyle.Success, disabled: true)
                    .WithButton("🔓 Release Ticket", "ticket_release", ButtonStyle.Secondary);

                await originalMessage.ModifyAsync(m =>
                {
                    m.Embed = embedBuilder.Build();
                    m.Components = claimButtons.Build();
                });

                await component.RespondAsync($"🎯 Ticket claimed by {user.Mention}.", ephemeral: false);
                break;

            case "ticket_release":
                
                var updatedEmbed = new EmbedBuilder();
                updatedEmbed.WithTitle(embedBuilder.Title)
                            .WithDescription(embedBuilder.Description)
                            .WithColor(embedBuilder.Color.GetValueOrDefault(Color.Orange))
                            .WithTimestamp(DateTimeOffset.UtcNow)
                            .WithFooter(embedBuilder.Footer?.Text, embedBuilder.Footer?.IconUrl);

                foreach (var field in embedBuilder.Fields.Where(f => f.Name != "👮 Claimed By"))
                    updatedEmbed.AddField(field.Name, field.Value, field.IsInline);

                var resetButtons = new ComponentBuilder()
                    .WithButton("🎯 Claim Ticket", "ticket_claim", ButtonStyle.Primary)
                    .WithButton("🔓 Release Ticket", "ticket_release", ButtonStyle.Secondary);

                await originalMessage.ModifyAsync(m =>
                {
                    m.Embed = updatedEmbed.Build();
                    m.Components = resetButtons.Build();
                });

                await component.RespondAsync($"🔓 Ticket released by {user.Mention}.", ephemeral: false);
                break;
        }
    }
}
