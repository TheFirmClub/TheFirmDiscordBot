using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class SocialsCommand : ISlashCommand
{
    public string Name => "socials";
    public string Description => "Displays all of our community's social media links";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🌐 The Firm — Social Media Links")
            .WithDescription("Stay connected with us on all of our platforms below:")
            .WithColor(Color.Purple)
            .WithThumbnailUrl("https://thefirm.club/Media/thefirm-thumb.png")
            .AddField("📢 Discord", "https://discord.gg/thefirmclub", true)
            .AddField("📸 Instagram", "https://www.instagram.com/thefirmrp", true)
            .AddField("▶️ YouTube", "https://youtube.com/TheFirmRP", true)
            .AddField("🐦 Twitter / X", "https://x.com/thefirmclubrp", true)
            .AddField("🎵 TikTok", "https://www.tiktok.com/@thefirmclub", true)
            .AddField("📘 Facebook", "https://www.facebook.com/profile.php?id=61579103063438", true)
            .WithFooter("The Firm • Building our community together")
            .WithCurrentTimestamp()
            .Build();


        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}