using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class SupportPanelSender
{
    private readonly ulong _channelId = 1393609914286342267; 

    public async Task SendSupportPanelAsync(DiscordSocketClient client)
    {
        var channel = client.GetChannel(_channelId) as IMessageChannel;
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("🎟️ How Can We Help?")
            .WithDescription("Welcome to our tickets channel! If you have any questions, concerns, or need assistance, please click the **‘Open Ticket’** button below to get in touch with our staff.")
            .WithImageUrl("https://live.staticflickr.com/65535/54683124276_7337ed1392_z.jpg") 
            .WithColor(Color.Blue)
            .Build();

        var button = new ButtonBuilder()
            .WithLabel("Open Ticket")
            .WithCustomId("open_ticket_panel")
            .WithStyle(ButtonStyle.Primary)
            .WithEmote(new Emoji("📩"));

        var component = new ComponentBuilder().WithButton(button);

        await channel.SendMessageAsync(embed: embed, components: component.Build());
    }
}