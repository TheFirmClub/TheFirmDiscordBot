using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class TicTacToeCommand : ISlashCommand
{
    public string Name => "tictactoe";
    public string Description => "Challenge another user to a game of Tic Tac Toe.";

    private static readonly Dictionary<ulong, GameState> ActiveGames = new(); // ChannelId -> GameState

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ You must use this command in a server.", ephemeral: true);
            return;
        }

        var opponentOption = command.Data.Options.FirstOrDefault(o => o.Name == "opponent")?.Value;
        if (opponentOption is not SocketGuildUser opponent)
        {
            await command.RespondAsync("❌ Please mention a valid user to challenge.", ephemeral: true);
            return;
        }

        if (opponent.Id == user.Id)
        {
            await command.RespondAsync("❌ You can't challenge yourself!", ephemeral: true);
            return;
        }

        if (ActiveGames.ContainsKey(command.Channel.Id))
        {
            await command.RespondAsync("❌ A game is already active in this channel.", ephemeral: true);
            return;
        }

        var game = new GameState(user.Id, opponent.Id);
        ActiveGames[command.Channel.Id] = game;

        var embed = new EmbedBuilder()
            .WithTitle("Tic Tac Toe")
            .WithDescription($"{MentionUser(game.CurrentPlayer)}'s turn (❌)")
            .WithColor(Color.Blue)
            .Build();

        var board = BuildBoard(game.Board, disabled: false);
        board.AddRow(new ActionRowBuilder().WithButton("End Game", "ttt_endgame", ButtonStyle.Danger));

        await command.RespondAsync($"{user.Mention} vs {opponent.Mention}",
            embed: embed,
            components: board.Build());
    }

    public static async Task HandleButton(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;

        if (customId == "ttt_endgame")
        {
            if (!ActiveGames.TryGetValue(component.Channel.Id, out var game))
            {
                await component.RespondAsync("There is no active game to end.", ephemeral: true);
                return;
            }

            if (component.User.Id != game.PlayerX && component.User.Id != game.PlayerO)
            {
                await component.RespondAsync("You're not part of this game.", ephemeral: true);
                return;
            }

            ActiveGames.Remove(component.Channel.Id);

            var embed = new EmbedBuilder()
                .WithTitle("Tic Tac Toe")
                .WithDescription($"Game ended by {component.User.Mention}.")
                .WithColor(Color.DarkRed)
                .Build();

            await component.UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = new ComponentBuilder().Build();
            });

            return;
        }

        if (!customId.StartsWith("ttt_")) return;

        var parts = customId.Split('_');
        if (parts.Length != 3 || !int.TryParse(parts[1], out int row) || !int.TryParse(parts[2], out int col))
            return;

        if (!ActiveGames.TryGetValue(component.Channel.Id, out var gameState))
        {
            await component.RespondAsync("No active game in this channel.", ephemeral: true);
            return;
        }

        if (component.User.Id != gameState.PlayerX && component.User.Id != gameState.PlayerO)
        {
            await component.RespondAsync("❌ You're not part of this game.", ephemeral: true);
            return;
        }

        if (component.User.Id != gameState.CurrentPlayer)
        {
            await component.RespondAsync("❌ It's not your turn!", ephemeral: true);
            return;
        }

        if (gameState.Board[row, col] != ' ')
        {
            await component.RespondAsync("❌ That cell is already taken.", ephemeral: true);
            return;
        }

        gameState.Board[row, col] = gameState.CurrentSymbol;
        gameState.SwitchTurn();

        string message;
        var win = gameState.CheckWin();
        var draw = gameState.CheckDraw();

        if (win)
        {
            message = $"🎉 Game over! {MentionUser(gameState.LastPlayer)} wins!";
            ActiveGames.Remove(component.Channel.Id);
        }
        else if (draw)
        {
            message = "🤝 Game over! It's a draw!";
            ActiveGames.Remove(component.Channel.Id);
        }
        else
        {
            message = $"{MentionUser(gameState.CurrentPlayer)}'s turn ({(gameState.CurrentSymbol == 'X' ? "❌" : "⭕")})";
        }

        var embedUpdate = new EmbedBuilder()
            .WithTitle("Tic Tac Toe")
            .WithDescription(message)
            .WithColor(win ? Color.Green : (draw ? Color.Orange : Color.Blue))
            .Build();

        var components = BuildBoard(gameState.Board, disabled: win || draw);
        if (!win && !draw)
        {
            components.AddRow(new ActionRowBuilder().WithButton("End Game", "ttt_endgame", ButtonStyle.Danger));
        }

        await component.UpdateAsync(msg =>
        {
            msg.Embed = embedUpdate;
            msg.Components = components.Build();
        });
    }

    private static ComponentBuilder BuildBoard(char[,] board, bool disabled)
    {
        var builder = new ComponentBuilder();

        for (int row = 0; row < 3; row++)
        {
            var actionRow = new ActionRowBuilder();

            for (int col = 0; col < 3; col++)
            {
                var label = board[row, col] switch
                {
                    'X' => "❌",
                    'O' => "⭕",
                    _ => "⬜"
                };

                actionRow.WithButton(
                    label: label,
                    customId: $"ttt_{row}_{col}",
                    style: ButtonStyle.Secondary,
                    emote: null,
                    disabled: disabled || board[row, col] != ' '
                );
            }

            builder.AddRow(actionRow);
        }

        return builder;
    }

    private static string MentionUser(ulong userId) => $"<@{userId}>";

    private class GameState
    {
        public ulong PlayerX { get; }
        public ulong PlayerO { get; }
        public ulong CurrentPlayer { get; private set; }
        public ulong LastPlayer { get; private set; }
        public char CurrentSymbol => CurrentPlayer == PlayerX ? 'X' : 'O';
        public char[,] Board { get; } = new char[3, 3]
        {
            { ' ', ' ', ' ' },
            { ' ', ' ', ' ' },
            { ' ', ' ', ' ' }
        };

        public GameState(ulong playerX, ulong playerO)
        {
            PlayerX = playerX;
            PlayerO = playerO;
            CurrentPlayer = new Random().Next(2) == 0 ? PlayerX : PlayerO;
        }

        public void SwitchTurn()
        {
            LastPlayer = CurrentPlayer;
            CurrentPlayer = CurrentPlayer == PlayerX ? PlayerO : PlayerX;
        }

        public bool CheckWin()
        {
            for (int i = 0; i < 3; i++)
            {
                if (Board[i, 0] != ' ' && Board[i, 0] == Board[i, 1] && Board[i, 1] == Board[i, 2]) return true;
                if (Board[0, i] != ' ' && Board[0, i] == Board[1, i] && Board[1, i] == Board[2, i]) return true;
            }
            return Board[0, 0] != ' ' && Board[0, 0] == Board[1, 1] && Board[1, 1] == Board[2, 2]
                || Board[0, 2] != ' ' && Board[0, 2] == Board[1, 1] && Board[1, 1] == Board[2, 0];
        }

        public bool CheckDraw()
        {
            foreach (var cell in Board)
                if (cell == ' ') return false;
            return !CheckWin();
        }
    }
}
