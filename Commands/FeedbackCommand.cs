using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class FeedbackCommand : ISlashCommand
{
    private readonly ulong _guildId = 1393589436402634874;

    // ✅ UPDATED CHANNEL
    private readonly ulong _feedbackChannelId = 1492725457190256710;

    private DiscordSocketClient _client;

    public string Name => "feedback";
    public string Description => "Submit server feedback.";

    public async Task RegisterAsync(DiscordSocketClient client)
    {
        _client = client;

        _client.InteractionCreated -= OnInteractionCreated;
        _client.InteractionCreated += OnInteractionCreated;

        await client.Rest.CreateGuildCommand(
            new SlashCommandBuilder()
                .WithName("feedback")
                .WithDescription("Submit feedback")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("new")
                    .WithDescription("Open feedback panel")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .Build(),
            _guildId
        );
    }

    // =========================
    // Slash Command
    // =========================
    public async Task ExecuteAsync(SocketSlashCommand cmd)
    {
        var sub = cmd.Data.Options.FirstOrDefault()?.Name;

        if (sub == "new")
        {
            var embed = new EmbedBuilder()
                .WithTitle("📩 Submit Feedback")
                .WithColor(Color.Blue)
                .WithDescription("Select a category below.");

            var buttons = new ComponentBuilder()
                .WithButton("Police", "fb:police", ButtonStyle.Primary)
                .WithButton("TFHS", "fb:tfhs", ButtonStyle.Primary)
                .WithButton("Civilians", "fb:civ", ButtonStyle.Primary)
                .WithButton("General", "fb:general", ButtonStyle.Secondary)
                .WithButton("Development", "fb:dev", ButtonStyle.Success);

            await cmd.RespondAsync(
                embed: embed.Build(),
                components: buttons.Build(),
                ephemeral: true
            );
        }
    }

    // =========================
    // Interaction Router
    // =========================
    private async Task OnInteractionCreated(SocketInteraction arg)
    {
        try
        {
            switch (arg)
            {
                case SocketMessageComponent c:

                    if (c.Data.CustomId.StartsWith("fb:"))
                        await OpenFeedbackModal(c);

                    break;

                case SocketModal m:

                    if (m.Data.CustomId.StartsWith("fb:submit"))
                        await SubmitFeedback(m);

                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Feedback] Error: {e}");
        }
    }

    // =========================
    // Open Modal
    // =========================
    private async Task OpenFeedbackModal(SocketMessageComponent comp)
    {
        string category = comp.Data.CustomId.Split(":")[1];

        var modal = new ModalBuilder()
            .WithTitle($"Feedback - {category.ToUpper()}")
            .WithCustomId($"fb:submit:{category}")
            .AddTextInput(
                "Your feedback",
                "text",
                TextInputStyle.Paragraph,
                placeholder: "Write your feedback here...",
                required: true
            );

        await comp.RespondWithModalAsync(modal.Build());
    }

    // =========================
    // Submit Feedback
    // =========================
    private async Task SubmitFeedback(SocketModal modal)
    {
        var parts = modal.Data.CustomId.Split(":");
        string category = parts[2];

        var text = modal.Data.Components.First().Value.Trim();

        ulong roleToPing = 0;
        string roleMention = "";

        switch (category)
        {
            case "police":
                roleToPing = 1420512528395665569;
                break;

            case "tfhs":
                roleToPing = 1420512797191704616;
                break;

            case "civ":
                roleToPing = 1420513009729802260;
                break;

            case "general":
                roleMention = "@here";
                break;

            case "dev":
                roleToPing = 1393733280523882546;
                break;
        }

        if (roleToPing != 0)
            roleMention = $"<@&{roleToPing}>";

        var embed = new EmbedBuilder()
            .WithTitle("📩 New Feedback")
            .WithColor(Color.Gold)
            .AddField("Category", category.ToUpper(), true)
            .AddField("User", modal.User.Mention, true)
            .AddField("Feedback", text)
            .WithFooter($"User ID: {modal.User.Id}")
            .WithCurrentTimestamp();

        var channel = _client
            .GetGuild(_guildId)
            .GetTextChannel(_feedbackChannelId);

        // ✅ Send message
        var msg = await channel.SendMessageAsync(
            roleMention,
            false,
            embed.Build()
        );

        // ✅ Create thread automatically
        await channel.CreateThreadAsync(
            name: $"{category.ToUpper()} Feedback - {modal.User.Username}",
            type: ThreadType.PublicThread,
            autoArchiveDuration: ThreadArchiveDuration.OneWeek,
            message: msg
        );

        await modal.RespondAsync(
            "✅ Your feedback has been submitted.",
            ephemeral: true
        );
    }
}