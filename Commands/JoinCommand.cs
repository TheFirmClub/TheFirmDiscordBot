using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class JoinCommand : ISlashCommand
{
    public string Name => "connect"; // 👈 changed from "howtojoin"
    public string Description => "Instructions to connect to The Firm FiveM server";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var embed = new EmbedBuilder()
            .WithTitle("👋 Welcome to The Firm")
            .WithDescription(
                "**To connect to our FiveM server:**\n\n" +
                "🔧 At the FiveM Home Screen, Press `F8` and enter:\n" +
                "`connect play.thefirm.club`\n\n" +
                "🌍 Or open the **Server List**, search for **The Firm**, and connect to the first result."
            )
            .WithColor(Color.DarkPurple)
            .WithFooter(footer => footer.Text = "The Firm")
            .WithCurrentTimestamp()
			.WithThumbnailUrl("https://i.ibb.co/M5Qs7SgK/Logo-Copy.png")
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}
