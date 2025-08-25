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
            .AddField("📢 Discord", "https://discord.thefirm.club", true)
            .AddField("📸 Instagram", "https://instagram.thefirm.club", true)
            .AddField("▶️ YouTube", "https://youtube.thefirm.club", true)
            .AddField("🐦 Twitter / X", "https://twitter.thefirm.club", true)
            .AddField("🎵 TikTok", "https://tiktok.thefirm.club", true)
            .AddField("📘 Facebook", "https://facebook.thefirm.club", true)
            .WithFooter("The Firm • Building our community together")
            .WithCurrentTimestamp()
            .Build();


        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}