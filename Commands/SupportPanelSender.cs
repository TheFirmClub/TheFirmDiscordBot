using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class SupportPanelSender
{
    private readonly ulong _channelId = 1393609914286342267; // 🔁 Replace with your support channel ID
    private const string PanelMessageIdentifier = "📩 Support Panel"; // Used to detect and clean old panels

    public async Task SendSupportPanelAsync(DiscordSocketClient client)
    {
        var channel = client.GetChannel(_channelId) as IMessageChannel;
        if (channel == null) return;

        // 🔍 Step 1: Remove any previous panel messages
        var messages = await channel.GetMessagesAsync(limit: 20).FlattenAsync();

        foreach (var message in messages)
        {
            if (message.Embeds.Any(e => e.Title?.Contains("How Can We Help?") == true) ||
                message.Content.Contains(PanelMessageIdentifier))
            {
                try
                {
                    await message.DeleteAsync();
                    Console.WriteLine("🧹 Old support panel deleted.");
                }
                catch
                {
                    Console.WriteLine("⚠️ Could not delete previous panel.");
                }
            }
        }

        // 🧱 Step 2: Build new embed
        var embed = new EmbedBuilder()
            .WithTitle("🎟️ How Can We Help?")
            .WithDescription("Welcome to our tickets channel! If you have any questions, concerns, or need assistance, please use the dropdown below to get in touch with our staff.")
            .WithImageUrl("https://thefirm.club/Media/helpdesk.png") // 🔁 Replace with your image
            .WithColor(Color.Blue)
            .Build();

        // 🎛️ Step 3: Build dropdown menu (same as /support)
        var menu = new SelectMenuBuilder()
            .WithCustomId("support_type_select")
            .WithPlaceholder("Choose support type")
            .AddOption("General Support", "general", "Questions or general help")
            .AddOption("Game Support", "game", "Issues with gameplay or features")
            .AddOption("Subscription Support", "sub", "Payment, perks, or upgrades")
            .AddOption("Report a Player", "reportplayer", "Report a player")
            .AddOption("Report a Staff Member", "reportstaff", "Report a Moderator / Staff Member");

        var component = new ComponentBuilder().WithSelectMenu(menu);

        // 📨 Step 4: Send the panel message
        await channel.SendMessageAsync(
            text: PanelMessageIdentifier,
            embed: embed,
            components: component.Build()
        );

        Console.WriteLine("✅ Support panel sent with dropdown menu.");
    }
}
