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
        1393729574537396355, // Senior Mod
        1393623589122736238, // Admin
        1393590761953558608  // Developer
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        var caller = (SocketGuildUser)command.User;

        // Get the "user" option from the command
        var userOption = command.Data.Options.FirstOrDefault(o => o.Name == "user")?.Value;
        if (userOption is not SocketUser targetUser)
        {
            await command.RespondAsync("❌ You must mention a user to slap.", ephemeral: true);
            return;
        }

        // 🔒 Role check
        bool hasAccess = caller.Roles.Any(role => allowedRoles.Contains(role.Id));
        if (!hasAccess)
        {
            await command.RespondAsync("⛔ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        // ❌ Self check
        if (targetUser.Id == caller.Id)
        {
            await command.RespondAsync("You can't slap yourself! 🤦", ephemeral: true);
            return;
        }

        // 🧠 Slap logic
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
        var msg = messages[rand.Next(messages.Length)];
        var gif = gifs[rand.Next(gifs.Length)];

        var embed = new EmbedBuilder()
            .WithTitle("👋 SLAP!")
            .WithDescription($"{caller.Mention} {msg} {targetUser.Mention}!")
            .WithImageUrl(gif)
            .WithColor(Color.Red)
            .WithCurrentTimestamp()
            .Build();

        await command.RespondAsync(embed: embed);
    }
}
