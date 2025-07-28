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

        // 🔤 Map internal value to user-facing label
        string label = selectedValue switch
        {
            "general" => "General Support",
            "game" => "Game Support",
            "ban" => "Ban Appeals",
            "sub" => "Subscription Support",
            _ => "Support"
        };

        // 🧾 Introductory message
        string intro = $"Thank you for creating a support ticket under **{label}**.\n" +
                       "A member of staff will be with you shortly.\n\n" +
                       "📌 *Please note: All conversations within this ticket are confidential.*\n\n" +
                       "📝 Let us know how we can help:";

        // 🧱 Build modal
        var modal = new ModalBuilder()
            .WithTitle($"{label} Ticket")
            .WithCustomId($"ticket_reason:{selectedValue}")
            .AddTextInput(intro, "ticket_reason_input", TextInputStyle.Paragraph, placeholder: "Describe your issue here...", required: true, maxLength: 400);

        await component.RespondWithModalAsync(modal.Build());
    }
}