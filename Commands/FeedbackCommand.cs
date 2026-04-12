using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class FeedbackCommand : ISlashCommand
{
    private readonly ulong _guildId = 1393589436402634874;
    private readonly ulong _channelId = 1492725457190256710;

    private DiscordSocketClient _client;

    // ✅ Cooldown system
    private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
    private const int CooldownSeconds = 3600; // 60 minutes

    public string Name => "feedback";
    public string Description => "Submit feedback.";

    public async Task RegisterAsync(DiscordSocketClient client)
    {
        _client = client;

        // ✅ Self-contained handler (SAFE filtering inside)
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
        // 🔒 Cooldown check BEFORE UI
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
    // Interaction Router (SAFE)
    // =====================
    private async Task OnInteractionCreated(SocketInteraction arg)
    {
        try
        {
            // ✅ ONLY handle feedback interactions
            if (arg is SocketMessageComponent c)
            {
                if (c.Data.CustomId != "fb:category")
                    return;

                await HandleCategory(c);
            }

            else if (arg is SocketModal m)
            {
                if (!m.Data.CustomId.StartsWith("fb:submit"))
                    return;

                await SubmitFeedback(m);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Feedback] {e}");
        }
    }

    // =====================
    // Dropdown → Modal
    // =====================
    private async Task HandleCategory(SocketMessageComponent comp)
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
    // Submit Feedback
    // =====================
    private async Task SubmitFeedback(SocketModal modal)
    {
        var userId = modal.User.Id;

        // 🔒 Cooldown check AGAIN (anti bypass)
        if (_cooldowns.TryGetValue(userId, out var last))
        {
            var diff = (DateTime.UtcNow - last).TotalSeconds;

            if (diff < CooldownSeconds)
            {
                var remaining = (int)(CooldownSeconds - diff);
                var minutes = (int)Math.Ceiling(remaining / 60.0);

                await modal.RespondAsync(
                    $"⛔ You must wait **{minutes} minute(s)** before submitting again.",
                    ephemeral: true
                );
                return;
            }
        }

        var category = modal.Data.CustomId.Split(":")[2];
        var text = modal.Data.Components.First().Value.Trim();

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

        var channel = _client
            .GetGuild(_guildId)
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

        // ✅ Apply cooldown AFTER success
        _cooldowns[userId] = DateTime.UtcNow;

        await modal.RespondAsync("✅ Feedback submitted.", ephemeral: true);
    }
}