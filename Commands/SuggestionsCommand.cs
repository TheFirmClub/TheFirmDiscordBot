using Discord;
using Discord.WebSocket;
using Discord.Net;
using MySqlConnector;
using System;
using System.Linq;
using System.Threading.Tasks;

public class SuggestionsCommand : ISlashCommand
{
    // ===== CONFIG =====
    private readonly ulong _guildId = 1393589436402634874;
    private readonly ulong _suggestionChannelId = 1451286531422814319;

    private static readonly ulong[] StaffRoleIds =
    {
        1420513009729802260,
        1393590761953558608
    };

    private readonly string _mysql =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;User ID=thefirmprod;Password=edr6BYZqmq7eud0mwm;SslMode=Required;AllowPublicKeyRetrieval=True;Character Set=utf8mb4;";

    private DiscordSocketClient _client;

    public string Name => "suggestions";
    public string Description => "Submit a server suggestion.";

    public async Task RegisterAsync(DiscordSocketClient client)
    {
        _client = client;

        _client.InteractionCreated -= OnInteractionCreated;
        _client.InteractionCreated += OnInteractionCreated;

        await client.Rest.CreateGuildCommand(
            new SlashCommandBuilder()
                .WithName(Name)
                .WithDescription(Description)
                .Build(),
            _guildId
        );
    }

    // ========= Slash =========
    public async Task ExecuteAsync(SocketSlashCommand cmd)
    {
        var embed = new EmbedBuilder()
            .WithTitle("📢 Suggestions")
            .WithColor(Color.Blue)
            .WithDescription(
                "• Be constructive\n" +
                "• One idea per suggestion\n" +
                "• Staff review all submissions\n\n" +
                "Click **Create** to continue."
            );

        var buttons = new ComponentBuilder()
            .WithButton("Create New Suggestion", "suggest:create", ButtonStyle.Primary)
            .WithButton("Exit", "suggest:exit", ButtonStyle.Secondary);

        await cmd.RespondAsync(embed: embed.Build(), components: buttons.Build(), ephemeral: true);
    }

    // ========= Router =========
    private async Task OnInteractionCreated(SocketInteraction arg)
    {
        try
        {
            switch (arg)
            {
                case SocketMessageComponent c:
                    if (c.Data.CustomId == "suggest:create")
                        await OpenModal(c);
                    else if (c.Data.CustomId.StartsWith("vote:"))
                        await HandleVote(c);
                    else if (c.Data.CustomId.StartsWith("staff:"))
                        await HandleStaffAction(c);
                    break;

                case SocketModal m:
                    if (m.Data.CustomId == "suggest:submit")
                        await SubmitSuggestion(m);
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Suggestions] Error: {e}");
        }
    }

    // ========= Modal =========
    private async Task OpenModal(SocketMessageComponent comp)
    {
        var modal = new ModalBuilder()
            .WithTitle("New Suggestion")
            .WithCustomId("suggest:submit")
            .AddTextInput("Suggestion", "text", TextInputStyle.Paragraph, required: true);

        await comp.RespondWithModalAsync(modal.Build());
    }

    // ========= Submit =========
    private async Task SubmitSuggestion(SocketModal modal)
    {
        var text = modal.Data.Components.First().Value.Trim();

        var embed = new EmbedBuilder()
            .WithTitle("💡 New Suggestion")
            .WithColor(Color.Gold)
            .AddField("Submitter", modal.User.Mention, true)
            .AddField("Suggestion", text)
            .AddField("Results so far", "✅ 0\n❌ 0", true)
            .WithFooter("Vote below or discuss in thread");

        var buttons = new ComponentBuilder()
            .WithButton("Upvote", "vote:up", ButtonStyle.Success)
            .WithButton("Downvote", "vote:down", ButtonStyle.Danger)
            .WithButton("Accept", "staff:accept", ButtonStyle.Primary)
            .WithButton("Deny", "staff:reject", ButtonStyle.Secondary);

        var channel = _client.GetGuild(_guildId).GetTextChannel(_suggestionChannelId);
        var msg = await channel.SendMessageAsync(embed: embed.Build(), components: buttons.Build());

        await channel.CreateThreadAsync(
            name: $"Discussion – Suggestion #{msg.Id}",
            type: ThreadType.PublicThread,
            autoArchiveDuration: ThreadArchiveDuration.OneWeek,
            message: msg
        );
        
        using var conn = new MySqlConnection(_mysql);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO suggestions (message_id, author_id, suggestion) VALUES (@m,@u,@s)";
        cmd.Parameters.AddWithValue("@m", (long)msg.Id);
        cmd.Parameters.AddWithValue("@u", (long)modal.User.Id);
        cmd.Parameters.AddWithValue("@s", text);
        await cmd.ExecuteNonQueryAsync();

        await modal.RespondAsync("✅ Suggestion submitted.", ephemeral: true);
    }

    // ========= Voting =========
    private async Task HandleVote(SocketMessageComponent comp)
    {
        bool vote = comp.Data.CustomId == "vote:up";

        using var conn = new MySqlConnection(_mysql);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"REPLACE INTO suggestion_votes (message_id, user_id, vote)
              VALUES (@m,@u,@v)";
        cmd.Parameters.AddWithValue("@m", (long)comp.Message.Id);
        cmd.Parameters.AddWithValue("@u", (long)comp.User.Id);
        cmd.Parameters.AddWithValue("@v", vote);
        await cmd.ExecuteNonQueryAsync();

        var count = conn.CreateCommand();
        count.CommandText =
            @"SELECT 
                SUM(vote=1), 
                SUM(vote=0) 
              FROM suggestion_votes WHERE message_id=@m";
        count.Parameters.AddWithValue("@m", (long)comp.Message.Id);

        using var r = await count.ExecuteReaderAsync();
        await r.ReadAsync();
        int up = r.IsDBNull(0) ? 0 : r.GetInt32(0);
        int down = r.IsDBNull(1) ? 0 : r.GetInt32(1);

        var eb = comp.Message.Embeds.First().ToEmbedBuilder();
        eb.Fields = eb.Fields.Where(f => f.Name != "Results so far").ToList();
        eb.AddField("Results so far", $"✅ **{up}**\n❌ **{down}**", true);

        await comp.Message.ModifyAsync(m => m.Embed = eb.Build());
        await comp.RespondAsync("Vote recorded.", ephemeral: true);
    }

    // ========= Staff Actions =========
    private async Task HandleStaffAction(SocketMessageComponent comp)
    {
        var guser = comp.User as SocketGuildUser;
        if (!guser.Roles.Any(r => StaffRoleIds.Contains(r.Id)))
        {
            await comp.RespondAsync("Staff only.", ephemeral: true);
            return;
        }

        string status = comp.Data.CustomId == "staff:accept" ? "ACCEPTED" : "REJECTED";
        var color = status == "ACCEPTED" ? Color.Green : Color.Red;

        using var conn = new MySqlConnection(_mysql);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE suggestions SET status=@s WHERE message_id=@m";
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@m", (long)comp.Message.Id);
        await cmd.ExecuteNonQueryAsync();

        var eb = comp.Message.Embeds.First().ToEmbedBuilder();
        eb.Color = color;
        eb.AddField("Final Decision", $"{status} by {comp.User.Mention}");

        await comp.Message.ModifyAsync(m =>
        {
            m.Embed = eb.Build();
            m.Components = new ComponentBuilder().Build(); // lock voting
        });

        await comp.RespondAsync($"Suggestion {status.ToLower()}.", ephemeral: true);
    }
}
