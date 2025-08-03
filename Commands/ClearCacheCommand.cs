using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

public class ClearCacheCommand : ISlashCommand
{
    public string Name => "clearcache";
    public string Description => "Steps to clear your FiveM cache and resolve common issues";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🧹 Clear Your FiveM Cache")
            .WithDescription(
                "**Based on the provided information, we recommend clearing your FiveM cache.**\n\n" +
                "Follow these steps carefully:\n\n" +
                "1️⃣ Press **Start/Win + R** to open the Run terminal.\n" +
                "2️⃣ Paste the following path and press Enter:\n" +
                "```%localappdata%\\FiveM\\FiveM.app\\data\\```\n" +
                "3️⃣ Delete **everything inside** this folder.\n" +
                "4️⃣ Re-open FiveM and connect to the server.\n" +
                "5️⃣ **Wander around the map** to re-build the cache. Textures and models may take time to load.\n" +
                "6️⃣ If the issue persists, let us know!"
            )
            .WithColor(Color.Orange)
            .WithFooter(footer => footer.Text = "The Firm Support")
            .WithCurrentTimestamp()
            .WithThumbnailUrl("https://i.ibb.co/M5Qs7SgK/Logo-Copy.png")
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: false);
    }
}
