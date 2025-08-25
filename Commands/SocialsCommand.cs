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
            .WithTitle("🌐 The Firm RP — Social Media")
            .WithDescription("Stay connected with us on all of our platforms below:")
            .WithColor(Color.Purple)
            .WithThumbnailUrl("https://thefirm.club/Media/thefirm-thumb.png")
            .AddField("📢 Discord", "https://discord.gg/thefirmclub", false)
            .AddField("📸 Instagram", "https://www.instagram.com/thefirmrp", false)
            .AddField("▶️ YouTube", "https://youtube.com/TheFirmRP", false)
            .AddField("🐦 Twitter / X", "https://x.com/thefirmclubrp", false)
            .AddField("🎵 TikTok", "https://www.tiktok.com/@thefirmclub", false)
            .AddField("📘 Facebook", "https://www.facebook.com/profile.php?id=61579103063438", false)
            .WithFooter("The Firm RP • Building our community together")
            .WithCurrentTimestamp()
            .Build();


        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}