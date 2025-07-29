using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class ForumLinkCommand : ISlashCommand
{
    public string Name => "forumlink";
    public string Description => "Instructions to verify and link your account to The Firm Forum";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var embed = new EmbedBuilder()
            .WithTitle("👋 Link your account to The Firm Forum")
            .WithDescription(
                "Please link your Discord to the The Firm forum by clicking this link below:\n\n" +
                "https://forum.thefirm.club/index.php?account/connected-accounts/"
            )
            .WithColor(Color.DarkPurple)
            .WithFooter(footer => footer.Text = "The Firm")
            .WithCurrentTimestamp()
            .WithThumbnailUrl("https://i.ibb.co/M5Qs7SgK/Logo-Copy.png")
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}
