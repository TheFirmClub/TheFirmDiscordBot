using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class CoinFlipCommand : ISlashCommand
{
    public string Name => "coinflip";
    public string Description => "Flips a coin.";

    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355,
        1393623589122736238,
        1393590761953558608
    };

    private bool HasPermission(SocketGuildUser user)
    {
        return user.Roles.Any(r => allowedRoles.Contains(r.Id));
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user || !HasPermission(user))
        {
            await command.RespondAsync("❌ You don’t have permission to use this command.", ephemeral: true);
            return;
        }

        string result = new Random().Next(2) == 0 ? "🪙 Heads" : "🪙 Tails";

        var embed = new EmbedBuilder()
            .WithTitle("Coin Flip")
            .WithDescription($"You flipped: **{result}**")
            .WithColor(Color.Gold)
            .WithFooter(f => f.Text = $"Requested by {user.Username}")
            .WithCurrentTimestamp();

        await command.RespondAsync(embed: embed.Build());
    }
}