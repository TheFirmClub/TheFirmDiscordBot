using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class RpsCommand : ISlashCommand
{
    public string Name => "rps";
    public string Description => "Challenge another user to Rock–Paper–Scissors.";

    // ChannelId -> state
    private static readonly Dictionary<ulong, GameState> ActiveGames = new();

    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355,
        1393623589122736238,
        1393590761953558608
    };

    private bool HasPermission(SocketGuildUser user) => user.Roles.Any(r => allowedRoles.Contains(r.Id));

    private enum Rps { Rock = 0, Paper = 1, Scissors = 2 }

    private record GameState
    (
        ulong MessageId,
        ulong ChannelId,
        ulong StarterId,
        ulong P1Id, // starter
        ulong P2Id, // opponent
        Dictionary<ulong, Rps?> Picks,
        bool Finished = false
    );

    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("opponent")
                .WithDescription("Who do you want to duel?")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ You must use this command in a server.", ephemeral: true);
            return;
        }
        if (!HasPermission(user))
        {
            await command.RespondAsync("❌ You don’t have permission to use this command.", ephemeral: true);
            return;
        }

        var opp = (SocketGuildUser)command.Data.Options.First().Value!;
        if (opp.Id == user.Id)
        {
            await command.RespondAsync("❌ You can’t challenge yourself.", ephemeral: true);
            return;
        }

        if (ActiveGames.ContainsKey(command.ChannelId!.Value))
        {
            await command.RespondAsync("⚠️ There’s already an active RPS game in this channel. Please wait.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Rock–Paper–Scissors")
            .WithDescription($"**Players**\n• {user.Mention}\n• {opp.Mention}\n\nEach of you, pick your move. Results will reveal when **both** have chosen.")
            .WithColor(Color.Blue)
            .Build();

        await command.RespondAsync(embed: embed, components: BuildButtons("pending"));

        var msg = await command.GetOriginalResponseAsync();

        var state = new GameState(
            MessageId: msg.Id,
            ChannelId: msg.Channel.Id,
            StarterId: user.Id,
            P1Id: user.Id,
            P2Id: opp.Id,
            Picks: new Dictionary<ulong, Rps?> { [user.Id] = null, [opp.Id] = null },
            Finished: false
        );

        ActiveGames[state.ChannelId] = state;

        await msg.ModifyAsync(m =>
        {
            m.Components = BuildButtons(state.MessageId.ToString());
        });
    }
    
    public static Task HandleButton(SocketMessageComponent component) => HandleComponentAsync(component);
    
    public static async Task HandleComponentAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId is null || !component.Data.CustomId.StartsWith("rps:"))
            return;
        
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length < 3) return;

        var action = parts[1];
        if (!ulong.TryParse(parts[2], out var messageId)) return;

        var channelId = component.Channel.Id;
        if (!ActiveGames.TryGetValue(channelId, out var state) || state.MessageId != messageId)
        {
            await component.RespondAsync("This RPS game is no longer active.", ephemeral: true);
            return;
        }
        if (state.Finished)
        {
            await component.RespondAsync("This RPS game already ended.", ephemeral: true);
            return;
        }

        if (action == "cancel")
        {
            if (component.User.Id != state.StarterId)
            {
                await component.RespondAsync("Only the game starter can cancel.", ephemeral: true);
                return;
            }
            
            ActiveGames.Remove(channelId);
            var cancelled = new EmbedBuilder()
                .WithTitle("Rock–Paper–Scissors")
                .WithDescription("🛑 Game cancelled by the starter.")
                .WithColor(Color.DarkGrey)
                .Build();

            await component.UpdateAsync(m =>
            {
                m.Embed = cancelled;
                m.Components = DisableButtons(messageId.ToString());
            });
            return;
        }

        if (action == "pick")
        {
            if (component.User.Id != state.P1Id && component.User.Id != state.P2Id)
            {
                await component.RespondAsync("You’re not a player in this game.", ephemeral: true);
                return;
            }

            if (state.Picks[component.User.Id] is not null)
            {
                await component.RespondAsync("You already picked. Waiting for the other player…", ephemeral: true);
                return;
            }

            if (parts.Length < 4) return;
            if (!Enum.TryParse<Rps>(parts[3], out var choice)) return;

            state.Picks[component.User.Id] = choice;
            
            if (state.Picks[state.P1Id] is Rps p1 && state.Picks[state.P2Id] is Rps p2)
            {
                var (resultText, color) = Result(p1, p2, state.P1Id, state.P2Id);

                var reveal = new EmbedBuilder()
                    .WithTitle("Rock–Paper–Scissors — Result")
                    .WithDescription(
                        $"**Players**\n• <@{state.P1Id}> vs <@{state.P2Id}>\n\n" +
                        $"**Choices**\n• <@{state.P1Id}>: {EmojiOf(p1)}\n• <@{state.P2Id}>: {EmojiOf(p2)}\n\n" +
                        $"**{resultText}**")
                    .WithColor(color)
                    .Build();
                
                ActiveGames.Remove(channelId);

                await component.UpdateAsync(m =>
                {
                    m.Embed = reveal;
                    m.Components = DisableButtons(messageId.ToString());
                });
            }
            else
            {
                await component.RespondAsync("✅ Your choice is locked. Waiting for the other player…", ephemeral: true);
            }
        }
    }

    // ===== Helpers =====

    private static (string text, Color color) Result(Rps p1, Rps p2, ulong p1Id, ulong p2Id)
    {
        if (p1 == p2) return ("It’s a draw! 🤝", Color.Gold);
        bool p1Wins = (p1, p2) switch
        {
            (Rps.Rock, Rps.Scissors) => true,
            (Rps.Paper, Rps.Rock) => true,
            (Rps.Scissors, Rps.Paper) => true,
            _ => false
        };
        return p1Wins
            ? ($"<@{p1Id}> wins! 🎉", Color.Green)
            : ($"<@{p2Id}> wins! 🎉", Color.Green);
    }

    private static string EmojiOf(Rps r) => r switch
    {
        Rps.Rock => "🪨 Rock",
        Rps.Paper => "📄 Paper",
        _ => "✂️ Scissors"
    };

    private static MessageComponent BuildButtons(string messageId)
    {
        var builder = new ComponentBuilder()
            .WithButton("🪨 Rock", $"rps:pick:{messageId}:{Rps.Rock}", ButtonStyle.Primary)
            .WithButton("📄 Paper", $"rps:pick:{messageId}:{Rps.Paper}", ButtonStyle.Primary)
            .WithButton("✂️ Scissors", $"rps:pick:{messageId}:{Rps.Scissors}", ButtonStyle.Primary)
            .WithButton("Cancel", $"rps:cancel:{messageId}", ButtonStyle.Danger);
        return builder.Build();
    }

    private static MessageComponent DisableButtons(string messageId)
    {
        var builder = new ComponentBuilder()
            .WithButton("🪨 Rock", $"rps:off:{messageId}:0", ButtonStyle.Secondary, disabled: true)
            .WithButton("📄 Paper", $"rps:off:{messageId}:1", ButtonStyle.Secondary, disabled: true)
            .WithButton("✂️ Scissors", $"rps:off:{messageId}:2", ButtonStyle.Secondary, disabled: true)
            .WithButton("Cancel", $"rps:off:{messageId}", ButtonStyle.Secondary, disabled: true);
        return builder.Build();
    }
}
