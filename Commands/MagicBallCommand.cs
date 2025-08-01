using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class Magic8BallCommand : ISlashCommand
{
    public string Name => "8ball";
    public string Description => "Ask the magic 8-ball a question.";

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

    private static readonly string[] responses = new[]
    {
        "Yes", "No", "Maybe", "Definitely", "Ask again later", "Absolutely", "I don't think so", "Try again 🤔"
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user || !HasPermission(user))
        {
            await command.RespondAsync("❌ You don’t have permission to use this command.", ephemeral: true);
            return;
        }

        string question = command.Data.Options.First().Value.ToString();
        string answer = responses[new Random().Next(responses.Length)];

        var embed = new EmbedBuilder()
            .WithTitle("🎱 Magic 8-Ball")
            .AddField("You asked:", question)
            .AddField("Answer:", answer)
            .WithColor(Color.Purple)
            .WithFooter(f => f.Text = $"Requested by {user.Username}")
            .WithCurrentTimestamp();

        await command.RespondAsync(embed: embed.Build());
    }
}