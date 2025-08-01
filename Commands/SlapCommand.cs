using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

public class SlapCommand : ISlashCommand
{
    public string Name => "slap";
    public string Description => "Slap another user in a fun way";

    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355, 
        1393623589122736238, 
        1393590761953558608  
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var caller = (SocketGuildUser)command.User;
        var userToSlap = (SocketUser)command.Data.Options.First().Value;

        bool hasAccess = caller.Roles.Any(role => allowedRoles.Contains(role.Id));
        if (!hasAccess)
        {
            await command.RespondAsync("⛔ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        if (userToSlap.Id == caller.Id)
        {
            await command.RespondAsync("You can't slap yourself! 🤦", ephemeral: true);
            return;
        }

        var messages = new[]
        {
            "just gave a thunderous slap to",
            "slapped the soul out of",
            "delivered a dramatic backhand to",
            "sent a shocking slap across the face of"
        };

        var gifs = new[]
        {
            "https://media.tenor.com/1mzkXqmuNkcAAAAC/anime-slap.gif",
            "https://media.tenor.com/IbRA9X4CZmwAAAAC/slap.gif",
            "https://media.tenor.com/uI5KjRYGo4kAAAAC/slap-anime.gif",
            "https://media.tenor.com/yheo1GGu3FwAAAAC/slap.gif"
        };

        var rand = new Random();
        var message = messages[rand.Next(messages.Length)];
        var gif = gifs[rand.Next(gifs.Length)];

        var embed = new EmbedBuilder()
            .WithTitle("👋 SLAP!")
            .WithDescription($"{caller.Mention} {message} {userToSlap.Mention}!")
            .WithImageUrl(gif)
            .WithColor(Color.Red)
            .WithCurrentTimestamp()
            .Build();

        await command.RespondAsync(embed: embed);
    }
}
