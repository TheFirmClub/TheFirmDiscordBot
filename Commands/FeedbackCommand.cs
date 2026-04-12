using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class FeedbackCommand : ISlashCommand
{
    private readonly ulong _guildId = 1393589436402634874;
    private readonly ulong _channelId = 1492725457190256710;
    private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
    private const int CooldownSeconds = 3600; // 5 minutes

    private DiscordSocketClient _client;

    public string Name => "feedback";
    public string Description => "Submit feedback.";

    public async Task RegisterAsync(DiscordSocketClient client)
    {
        _client = client;

        _client.InteractionCreated -= OnInteractionCreated;
        _client.InteractionCreated += OnInteractionCreated;

        await client.Rest.CreateGuildCommand(
            new SlashCommandBuilder()
                .WithName("feedback")
                .WithDescription("Submit feedback")
                .Build(),
            _guildId
        );
    }

    // =====================
    // Slash
    // =====================
    public async Task ExecuteAsync(SocketSlashCommand cmd)
    {
        // 🔴 ADD THIS BLOCK FIRST
        if (_cooldowns.TryGetValue(cmd.User.Id, out var last))
        {
            var diff = (DateTime.UtcNow - last).TotalSeconds;

            if (diff < CooldownSeconds)
            {
                var remaining = (int)(CooldownSeconds - diff);
                var minutes = (int)Math.Ceiling(remaining / 60.0);

                await cmd.RespondAsync(
                    $"⛔ You're on cooldown for **{minutes} minute(s)**.",
                    ephemeral: true
                );
                return;
            }
        }
        
        var embed = new EmbedBuilder()
            .WithTitle("📩 Submit Feedback")
            .WithDescription("Select a category below.")
            .WithColor(Color.Blue);

        var menu = new SelectMenuBuilder()
            .WithCustomId("fb:category")
            .WithPlaceholder("Choose a category...")
            .AddOption("Police", "police")
            .AddOption("TFHS", "tfhs")
            .AddOption("Civilians", "civ")
            .AddOption("General", "general")
            .AddOption("Development", "dev");

        var components = new ComponentBuilder()
            .WithSelectMenu(menu);

        await cmd.RespondAsync(
            embed: embed.Build(),
            components: components.Build(),
            ephemeral: true
        );
    }

    // =====================
    // Interaction Router
    // =====================
    private async Task OnInteractionCreated(SocketInteraction arg)
    {
        try
        {
            switch (arg)
            {
                case SocketMessageComponent c:
                    if (c.Data.CustomId == "fb:category")
                        await HandleCategorySelect(c);
                    break;

                case SocketModal m:
                    if (m.Data.CustomId.StartsWith("fb:submit"))
                        await SubmitFeedback(m);
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Feedback] {e}");
        }
    }

    // =====================
    // Handle dropdown
    // =====================
    private async Task HandleCategorySelect(SocketMessageComponent comp)
    {
        var category = comp.Data.Values.First();

        var modal = new ModalBuilder()
            .WithTitle($"Feedback - {category.ToUpper()}")
            .WithCustomId($"fb:submit:{category}")
            .AddTextInput(
                "Your feedback",
                "text",
                TextInputStyle.Paragraph,
                required: true
            );

        await comp.RespondWithModalAsync(modal.Build());
    }

    // =====================
    // Submit
    // =====================
    private async Task SubmitFeedback(SocketModal modal)
    {
        var userId = modal.User.Id;

        if (_cooldowns.TryGetValue(userId, out var lastTime))
        {
            var diff = (DateTime.UtcNow - lastTime).TotalSeconds;

            if (diff < CooldownSeconds)
            {
                var remaining = (int)(CooldownSeconds - diff);

                await modal.RespondAsync(
                    $"⛔ You must wait **{remaining}s** before submitting another feedback.",
                    ephemeral: true
                );
                return;
            }
        }
        
        var category = modal.Data.CustomId.Split(":")[2];
        var text = modal.Data.Components.First().Value;

        ulong roleId = 0;
        string mention = "";

        switch (category)
        {
            case "police":
                roleId = 1420512528395665569;
                break;
            case "tfhs":
                roleId = 1420512797191704616;
                break;
            case "civ":
                roleId = 1420513009729802260;
                break;
            case "general":
                mention = "@here";
                break;
            case "dev":
                roleId = 1393733280523882546;
                break;
        }

        if (roleId != 0)
            mention = $"<@&{roleId}>";

        var embed = new EmbedBuilder()
            .WithTitle("📩 New Feedback")
            .WithColor(Color.Gold)
            .AddField("Category", category.ToUpper(), true)
            .AddField("User", modal.User.Mention, true)
            .AddField("Feedback", text)
            .WithCurrentTimestamp();

        var channel = _client.GetGuild(_guildId)
                             .GetTextChannel(_channelId);

        var msg = await channel.SendMessageAsync(
            mention,
            false,
            embed.Build()
        );

        await channel.CreateThreadAsync(
            $"{category.ToUpper()} - {modal.User.Username}",
            ThreadType.PublicThread,
            ThreadArchiveDuration.OneWeek,
            message: msg
        );

        await modal.RespondAsync("✅ Feedback submitted.", ephemeral: true);
        _cooldowns[userId] = DateTime.UtcNow;
    }
}