using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Linq;

public class SendImageModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ulong TargetChannelId = 1393622296454893619; // target channel

    private readonly ulong[] AllowedRoleIds =
    {
        1393590761953558600, // SN
        1399173940622135448, // SLT
        1463090510406225991, // Operational COMMAND
        1399174001275965520, // AREA COMMAND
        
    };

    [MessageCommand("Send Image")]
    public async Task SendImageAsync(IMessage message)
    {
        var user = Context.User as SocketGuildUser;

        if (user == null || !user.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await RespondAsync("You do not have permission to use this.", ephemeral: true);
            return;
        }

        var image = message.Attachments.FirstOrDefault(a =>
            a.ContentType != null && a.ContentType.StartsWith("image/")
        );

        if (image == null)
        {
            await RespondAsync("That message does not contain an image.", ephemeral: true);
            return;
        }

        // Store image info inside modal custom id
        await RespondWithModalAsync<SendImageModal>(
            $"send_image_modal:{message.Id}:{image.Url}:{image.Filename}"
        );
    }

    [ModalInteraction("send_image_modal:*:*:*")]
    public async Task HandleSendImageModalAsync(
        ulong messageId,
        string imageUrl,
        string fileName,
        SendImageModal modal)
    {
        var channel = Context.Guild.GetTextChannel(TargetChannelId);

        if (channel == null)
        {
            await RespondAsync("Target channel not found.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Image Submission")
            .WithDescription(string.IsNullOrWhiteSpace(modal.Comment)
                ? "No comment provided."
                : modal.Comment)
            .WithImageUrl(imageUrl)
            .WithColor(Color.Blue)
            .AddField("Sent By", Context.User.Mention, true)
            .AddField("File Name", fileName, true)
            .WithFooter($"Original Message ID: {messageId}")
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);

        await RespondAsync("Image sent successfully.", ephemeral: true);
    }
}

public class SendImageModal : IModal
{
    public string Title => "Send Image";

    [InputLabel("Comment")]
    [ModalTextInput(
        "comment",
        TextInputStyle.Paragraph,
        "Add a comment for this image...",
        maxLength: 1000)]
    public string Comment { get; set; }
}