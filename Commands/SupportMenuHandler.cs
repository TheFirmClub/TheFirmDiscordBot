using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class SupportMenuHandler
{
    public async Task HandleAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId != "support_type_select") return;

        var selectedType = component.Data.Values.First(); // e.g. "general", "ban", etc.

        var modal = new ModalBuilder()
            .WithTitle("Support Ticket Reason")
            .WithCustomId($"ticket_reason:{selectedType}")
            .AddTextInput("What do you need help with?", "ticket_reason_input", TextInputStyle.Paragraph, required: true, maxLength: 400);

        await component.RespondWithModalAsync(modal.Build());
    }
}