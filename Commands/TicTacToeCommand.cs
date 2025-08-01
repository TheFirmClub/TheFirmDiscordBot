using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class TicTacToeCommand : ISlashCommand
{
    public string Name => "tictactoe";
    public string Description => "Challenge another player to a game of TicTacToe!";

    private static readonly Dictionary<ulong, TicTacToeGame> activeGames = new();

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var challenger = (SocketGuildUser)command.User;
        var opponent = (SocketGuildUser)command.Data.Options.First().Value;

        if (challenger.Id == opponent.Id)
        {
            await command.RespondAsync("❌ You can't challenge yourself!", ephemeral: true);
            return;
        }

        var game = new TicTacToeGame(challenger, opponent);
        var embed = game.BuildGameEmbed();
        var components = game.BuildGameButtons();

        await command.RespondAsync(embed: embed, components: components);
        var response = await command.GetOriginalResponseAsync();

        game.MessageId = response.Id;

        activeGames[response.Id] = game;
    }

    public static async Task HandleButton(SocketMessageComponent component)
    {
        if (!activeGames.TryGetValue(component.Message.Id, out var game))
            return;

        await game.HandleButton(component);

        if (game.IsFinished)
            activeGames.Remove(component.Message.Id);
    }

    // Internal TicTacToeGame class
    private class TicTacToeGame
    {
        public SocketGuildUser Player1 { get; }
        public SocketGuildUser Player2 { get; }
        public ulong MessageId { get; set; }

        private readonly char[,] board = new char[3, 3];
        private SocketGuildUser currentPlayer;
        public bool IsFinished { get; private set; }

        public TicTacToeGame(SocketGuildUser p1, SocketGuildUser p2)
        {
            Player1 = p1;
            Player2 = p2;
            currentPlayer = Player1;

            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    board[x, y] = ' ';
        }

        public Embed BuildGameEmbed()
        {
            var desc = BuildBoardVisual();
            return new EmbedBuilder()
                .WithTitle("Tic Tac Toe")
                .WithDescription(desc)
                .WithColor(Color.Blue)
                .WithFooter($"{currentPlayer.Username}'s turn ({GetSymbol(currentPlayer)})")
                .Build();
        }

        public MessageComponent BuildGameButtons()
        {
            var builder = new ComponentBuilder();
            for (int y = 0; y < 3; y++)
            {
                var row = new ActionRowBuilder();
                for (int x = 0; x < 3; x++)
                {
                    string label = board[x, y] == ' ' ? "⬜" : board[x, y] == 'X' ? "❌" : "⭕";
                    row.WithButton(label, $"{x},{y}", ButtonStyle.Secondary, disabled: board[x, y] != ' ');
                }
                builder.AddRow(row);
            }
            return builder.Build();
        }

        public async Task HandleButton(SocketMessageComponent component)
        {
            var coords = component.Data.CustomId.Split(',');
            int x = int.Parse(coords[0]);
            int y = int.Parse(coords[1]);

            if (component.User.Id != currentPlayer.Id)
            {
                await component.RespondAsync("❌ It's not your turn!", ephemeral: true);
                return;
            }

            if (board[x, y] != ' ')
            {
                await component.RespondAsync("❌ That spot is already taken!", ephemeral: true);
                return;
            }

            board[x, y] = GetSymbol(currentPlayer);

            if (CheckWin())
            {
                IsFinished = true;
                await component.UpdateAsync(msg =>
                {
                    msg.Embed = new EmbedBuilder()
                        .WithTitle("Tic Tac Toe")
                        .WithDescription(BuildBoardVisual())
                        .WithColor(Color.Green)
                        .WithFooter($"{currentPlayer.Username} wins!")
                        .Build();
                    msg.Components = new ComponentBuilder().Build();
                });
                return;
            }

            if (IsDraw())
            {
                IsFinished = true;
                await component.UpdateAsync(msg =>
                {
                    msg.Embed = new EmbedBuilder()
                        .WithTitle("Tic Tac Toe")
                        .WithDescription(BuildBoardVisual())
                        .WithColor(Color.Orange)
                        .WithFooter("It's a draw!")
                        .Build();
                    msg.Components = new ComponentBuilder().Build();
                });
                return;
            }

            currentPlayer = currentPlayer.Id == Player1.Id ? Player2 : Player1;

            await component.UpdateAsync(msg =>
            {
                msg.Embed = BuildGameEmbed();
                msg.Components = BuildGameButtons();
            });
        }


        private char GetSymbol(SocketGuildUser user) => user.Id == Player1.Id ? 'X' : 'O';

        private string BuildBoardVisual()
        {
            var sb = new StringBuilder();
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    sb.Append(board[x, y] == ' ' ? "⬜" : board[x, y] == 'X' ? "❌" : "⭕");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private bool IsDraw()
        {
            foreach (var cell in board)
                if (cell == ' ') return false;
            return true;
        }

        private bool CheckWin()
        {
            for (int i = 0; i < 3; i++)
            {
                if (board[i, 0] != ' ' && board[i, 0] == board[i, 1] && board[i, 1] == board[i, 2]) return true;
                if (board[0, i] != ' ' && board[0, i] == board[1, i] && board[1, i] == board[2, i]) return true;
            }

            if (board[0, 0] != ' ' && board[0, 0] == board[1, 1] && board[1, 1] == board[2, 2]) return true;
            if (board[0, 2] != ' ' && board[0, 2] == board[1, 1] && board[1, 1] == board[2, 0]) return true;

            return false;
        }
    }
}
