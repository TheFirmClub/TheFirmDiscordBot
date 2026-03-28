using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

// Aliases to avoid conflicts
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

        // Basic validation
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http"))
        {
            await command.RespondAsync("❌ Invalid URL. Must be a direct image link.", ephemeral: true);
            return;
        }

        using var http = new HttpClient();

        try
        {
            using var stream = await http.GetStreamAsync(url);

            // ⚠️ FIX: no 'using var' so we can reassign
            var image = Drawing.Image.FromStream(stream);

            // Resize large images (prevent crashes)
            if (image.Width > 2000 || image.Height > 2000)
            {
                image = new Drawing.Bitmap(image, new Drawing.Size(1000, 1000));
            }

            using (image)
            using (var graphics = Drawing.Graphics.FromImage(image))
            {
                var font = new Drawing.Font(Drawing.FontFamily.GenericSansSerif, image.Width / 10, Drawing.FontStyle.Bold);
                var sf = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center };

                // TOP TEXT
                DrawText(graphics, topText.ToUpper(), font,
                    new Drawing.RectangleF(0, 0, image.Width, image.Height / 4));

                // BOTTOM TEXT
                DrawText(graphics, bottomText.ToUpper(), font,
                    new Drawing.RectangleF(0, image.Height - (image.Height / 4), image.Width, image.Height / 4));

                var path = $"meme_{Guid.NewGuid()}.png";
                image.Save(path, DrawingImaging.ImageFormat.Png);

                await command.RespondWithFileAsync(path);

                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            await command.RespondAsync($"❌ Failed to process image: {ex.Message}", ephemeral: true);
        }
    }

    private void DrawText(Drawing.Graphics g, string text, Drawing.Font font, Drawing.RectangleF rect)
    {
        var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
            FormatFlags = Drawing.StringFormatFlags.LineLimit
        };
        var size = g.MeasureString(text, font, rect.Size);

        while ((size.Width > rect.Width || size.Height > rect.Height) && font.Size > 10)
        {
            font = new Drawing.Font(font.FontFamily, font.Size - 2, Drawing.FontStyle.Bold);
            size = g.MeasureString(text, font, rect.Size);
        }

        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                g.DrawString(text, font, Drawing.Brushes.Black,
                    new Drawing.RectangleF(rect.X + x, rect.Y + y, rect.Width, rect.Height),
                    format);
            }
        }

        g.DrawString(text, font, Drawing.Brushes.White, rect, format);
    }
}