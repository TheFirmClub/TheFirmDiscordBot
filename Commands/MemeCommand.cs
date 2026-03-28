using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

public class MemeCommand : ISlashCommand
{
    public string Name => "meme";
    public string Description => "Create a meme from an image URL";

    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .AddOption("url", ApplicationCommandOptionType.String, "Direct image link (jpg/png)", true)
            .AddOption("top", ApplicationCommandOptionType.String, "Top text", true)
            .AddOption("bottom", ApplicationCommandOptionType.String, "Bottom text", true)
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var url = command.Data.Options.First(x => x.Name == "url").Value.ToString();
        var topText = command.Data.Options.First(x => x.Name == "top").Value.ToString();
        var bottomText = command.Data.Options.First(x => x.Name == "bottom").Value.ToString();

        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http"))
        {
            await command.RespondAsync("❌ Invalid URL.", ephemeral: true);
            return;
        }

        using var http = new HttpClient();

        try
        {
            await command.DeferAsync();

            using var stream = await http.GetStreamAsync(url);
            var image = Drawing.Image.FromStream(stream);

            if (image.Width > 2000 || image.Height > 2000)
            {
                image = new Drawing.Bitmap(image, new Drawing.Size(1000, 1000));
            }

            using (image)
            using (var graphics = Drawing.Graphics.FromImage(image))
            {
                graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAlias;

                DrawText(graphics, topText.ToUpper(),
                    new Drawing.RectangleF(0, 0, image.Width, image.Height / 3));

                DrawText(graphics, bottomText.ToUpper(),
                    new Drawing.RectangleF(0, image.Height - (image.Height / 3), image.Width, image.Height / 3));

                var path = $"meme_{Guid.NewGuid()}.png";
                image.Save(path, DrawingImaging.ImageFormat.Png);

                await command.FollowupWithFileAsync(path);

                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            await command.RespondAsync($"❌ Failed: {ex.Message}", ephemeral: true);
        }
    }

    private void DrawText(Drawing.Graphics g, string text, Drawing.RectangleF rect)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center
        };

        float fontSize = Math.Max(24, rect.Height / 3);
        using var font = new Drawing.Font("Arial", fontSize, Drawing.FontStyle.Bold);

        for (int x = -3; x <= 3; x++)
        {
            for (int y = -3; y <= 3; y++)
            {
                g.DrawString(text, font, Drawing.Brushes.Black,
                    new Drawing.RectangleF(rect.X + x, rect.Y + y, rect.Width, rect.Height),
                    format);
            }
        }

        g.DrawString(text, font, Drawing.Brushes.White, rect, format);
    }
}