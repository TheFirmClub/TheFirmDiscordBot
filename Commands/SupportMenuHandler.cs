using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class SupportMenuHandler
{
    public async Task HandleAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId != "support_type_select") return;

        var selectedValue = component.Data.Values.First(); // e.g., "ban", "general", etc.

        string label = selectedValue switch
        {
            "general" => "General Support",
            "game" => "Game Support",
            "ban" => "Ban Appeals",
            "sub" => "Subscription Support",
            _ => "Support"
        };

        string modalTitle = $"{label} Ticket";
        string inputLabel = "Describe your issue";
        
        var modal = new ModalBuilder()
            .WithTitle(modalTitle)
            .WithCustomId($"ticket_reason:{selectedValue}")
            .AddTextInput(inputLabel, "ticket_reason_input", TextInputStyle.Paragraph, placeholder: "Please explain the issue clearly...", required: true, maxLength: 400);

        await component.RespondWithModalAsync(modal.Build());
    }
}