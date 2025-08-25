using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class SocialsCommand : ISlashCommand
{
    public string Name => "socials";
    public string Description => "Displays all of our community's social media links";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var description =
            "📢 **Discord:** https://discord.thefirm.club\n" +
            "📸 **Instagram:** https://instagram.thefirm.club\n" +
            "▶️ **YouTube:** https://youtube.thefirm.club\n" +
            "🐦 **Twitter / X:** https://twitter.thefirm.club\n" +
            "🎵 **TikTok:** https://tiktok.thefirm.club\n" +
            "📘 **Facebook:** https://facebook.thefirm.club";

        var embed = new EmbedBuilder()
            .WithTitle("🌐 The Firm — Social Media Links")
            .WithDescription(description)
            .WithColor(Color.Purple)
            .WithThumbnailUrl("https://thefirm.club/Media/thefirm-thumb.png")
            .WithFooter("The Firm • Building our community together")
            .WithCurrentTimestamp()
            .Build();


        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}