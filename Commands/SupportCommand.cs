using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class SupportCommand : ISlashCommand
{
    public string Name => "support";
    public string Description => "Create a support ticket";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId("support_type_select")
            .WithPlaceholder("Choose support type")
            .AddOption("General Support", "general")
            .AddOption("Game Support", "game")
            .AddOption("Subscription Support", "sub")
            .AddOption("Report a player", "reportplayer")
            .AddOption("Report a staff", "reportstaff");
        

        var builder = new ComponentBuilder().WithSelectMenu(menu);
        await command.RespondAsync("Please choose your support category:", components: builder.Build(), ephemeral: true);
    }
}