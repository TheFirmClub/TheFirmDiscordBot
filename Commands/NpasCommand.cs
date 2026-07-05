using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public abstract class BaseNpasCommand : ISlashCommand
{
    protected const ulong PilotRole = 1407310163467174028;
    protected const ulong TfoRole = 1521018719059443743;
    protected const ulong CommandChannel = 1521602870187659304;

    public abstract string Name { get; }
    public abstract string Description { get; }

    protected abstract ulong AllowedRole { get; }
    protected abstract ulong PingRole { get; }
    protected abstract string EmbedTitle { get; }
    protected abstract string RequestedRoleName { get; }

    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ You must use this command in a server.", ephemeral: true);
            return;
        }
        
        if (command.Channel.Id != CommandChannel)
        {
            await command.RespondAsync(
                $"❌ This command can only be used in <#{CommandChannel}>.",
                ephemeral: true);
            return;
        }

        if (!user.Roles.Any(r => r.Id == AllowedRole))
        {
            await command.RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(EmbedTitle)
            .WithDescription($"{user.Mention} is requesting a {RequestedRoleName} to deploy NPAS, please confirm if you are available?")
            .WithColor(Color.Blue)
            .Build();

        await command.RespondAsync(
            text: $"<@&{PingRole}>",
            embed: embed,
            allowedMentions: new AllowedMentions
            {
                RoleIds = new List<ulong> { PingRole }
            });
    }
}

public class PilotCommand : BaseNpasCommand
{
    public override string Name => "pilot";
    public override string Description => "Request a pilot to deploy NPAS.";

    protected override ulong AllowedRole => TfoRole;
    protected override ulong PingRole => PilotRole;
    protected override string EmbedTitle => "🚁 NPAS Pilot Request";
    protected override string RequestedRoleName => "pilot";
}

public class TfoCommand : BaseNpasCommand
{
    public override string Name => "tfo";
    public override string Description => "Request a TFO to deploy NPAS.";

    protected override ulong AllowedRole => PilotRole;
    protected override ulong PingRole => TfoRole;
    protected override string EmbedTitle => "👮 NPAS TFO Request";
    protected override string RequestedRoleName => "TFO";
}