using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Collections.Concurrent;
using System.Linq;

public class SendImageModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ulong TargetChannelId = 1393622296454893619;

    private readonly ulong[] AllowedRoleIds =
    {
        1393590761953558600, // SN
        1399173940622135448, // SLT
        1463090510406225991, // Operational COMMAND
        1399174001275965520, // AREA COMMAND
    };

    // Store images temporarily between command → modal
    private static readonly ConcurrentDictionary<ulong, (string Url, string FileName)> PendingImages = new();

    // =========================
    // RIGHT CLICK COMMAND
    // =========================
    [MessageCommand("Send Image")]
    public async Task SendImageAsync(IMessage message)
    {
        var user = Context.User as SocketGuildUser;

        // Permission check
        if (user == null || !user.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await RespondAsync("You do not have permission to use this.", ephemeral: true);
            return;
        }

        // Get image
        var image = message.Attachments.FirstOrDefault(a =>
            a.ContentType != null && a.ContentType.StartsWith("image/")
        );

        if (image == null)
        {
            await RespondAsync("That message does not contain an image.", ephemeral: true);
            return;
        }

        // Store image for modal step
        PendingImages[message.Id] = (image.Url, image.Filename);

        // Build modal manually (avoids "invalid modal" issues)
        var modal = new ModalBuilder()
            .WithTitle("Send Image")
            .WithCustomId($"send_image_modal:{message.Id}")
            .AddTextInput(
                label: "Comment",
                customId: "comment",
                style: TextInputStyle.Paragraph,
                placeholder: "Add a comment for this image...",
                required: false,
                maxLength: 1000
            )
            .Build();

        await RespondWithModalAsync(modal);
    }

    // =========================
    // MODAL HANDLER
    // =========================
    [ModalInteraction("send_image_modal:*")]
    public async Task HandleSendImageModalAsync(ulong messageId)
    {
        var modal = (SocketModal)Context.Interaction;

        var comment = modal.Data.Components
            .FirstOrDefault(x => x.CustomId == "comment")?.Value;

        // Retrieve stored image
        if (!PendingImages.TryRemove(messageId, out var imageData))
        {
            await RespondAsync("Image data expired or was not found.", ephemeral: true);
            return;
        }

        var channel = Context.Guild.GetTextChannel(TargetChannelId);

        if (channel == null)
        {
            await RespondAsync("Target channel not found.", ephemeral: true);
            return;
        }

        // Build embed
        var embed = new EmbedBuilder()
            .WithTitle("Image Submission")
            .WithDescription(string.IsNullOrWhiteSpace(comment)
                ? "No comment provided."
                : comment)
            .WithImageUrl(imageData.Url)
            .WithColor(Color.Blue)
            .AddField("Sent By", Context.User.Mention, true)
            .AddField("File Name", imageData.FileName, true)
            .WithFooter($"Message ID: {messageId}")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);

        await RespondAsync("Image sent successfully.", ephemeral: true);
    }
}