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
            "reportplayer" => "Report a Player",
            "sub" => "Subscription Support",
            "reportstaff" => "Report a staff",
            _ => "Support"
        };

        string modalTitle = $"{label} Ticket";

        var modal = new ModalBuilder()
            .WithTitle(modalTitle)
            .WithCustomId($"ticket_reason:{selectedValue}");

        // Add inputs depending on the type
        if (selectedValue == "reportplayer")
        {
            modal.AddTextInput("Character Name of Player", "staff_report_charname", 
                TextInputStyle.Short, 
                placeholder: "Enter the player’s character name", 
                required: true);

            modal.AddTextInput("Please provide a link to your clip / evidence", "staff_report_evidence", 
                TextInputStyle.Short, 
                placeholder: "Paste a valid link (e.g., YouTube, Medal, Streamable)", 
                required: true);

            modal.AddTextInput("Describe your issue", "ticket_reason_input",
                TextInputStyle.Paragraph,
                placeholder: "Please explain the incident clearly...",
                required: true,
                maxLength: 400);
        }
        else
        {
            modal.AddTextInput("Describe your issue", "ticket_reason_input",
                TextInputStyle.Paragraph,
                placeholder: "Please explain the issue clearly...",
                required: true,
                maxLength: 400);
        }

        await component.RespondWithModalAsync(modal.Build());
    }
}
