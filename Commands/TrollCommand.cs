using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

#region Shared Config + Utils

internal static class TrollConfig
{
    // 👇 Add/remove role IDs that are allowed to use ALL troll commands
    public static readonly ulong[] AllowedRoleIds = new ulong[]
    {
        1393729574537396355, // Game Mod
        1393623589122736238, // Discord Mod
        1393590761953558608  // SM
    };

    // Optional: Per-command overrides (leave empty to use global AllowedRoleIds)
    public static readonly Dictionary<string, ulong[]> PerCommandAllow = new()
    {
        // { "hack", new ulong[] { /* ... */ } },
        // { "fakeban", new ulong[] { /* ... */ } },
    };
}

internal static class TrollUtils
{
    private static readonly Random Rng = new();

    public static T Pick<T>(IReadOnlyList<T> list) => list[Rng.Next(list.Count)];

    public static string ReverseString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }

    // Tiny/superscript map (Latin best-effort)
    private static readonly Dictionary<char, char> TinyMap = new()
    {
        ['a']='ᵃ',['b']='ᵇ',['c']='ᶜ',['d']='ᵈ',['e']='ᵉ',['f']='ᶠ',['g']='ᵍ',['h']='ʰ',
        ['i']='ᶦ',['j']='ʲ',['k']='ᵏ',['l']='ˡ',['m']='ᵐ',['n']='ⁿ',['o']='ᵒ',['p']='ᵖ',
        ['q']='ᑫ',['r']='ʳ',['s']='ˢ',['t']='ᵗ',['u']='ᵘ',['v']='ᵛ',['w']='ʷ',['x']='ˣ',
        ['y']='ʸ',['z']='ᶻ',
        ['A']='ᴬ',['B']='ᴮ',['C']='ᶜ',['D']='ᴰ',['E']='ᴱ',['F']='ᶠ',['G']='ᴳ',['H']='ᴴ',
        ['I']='ᴵ',['J']='ᴶ',['K']='ᴷ',['L']='ᴸ',['M']='ᴹ',['N']='ᴺ',['O']='ᴼ',['P']='ᴾ',
        ['Q']='ᑫ',['R']='ᴿ',['S']='ˢ',['T']='ᵀ',['U']='ᵁ',['V']='ⱽ',['W']='ᵂ',['X']='ˣ',
        ['Y']='ʸ',['Z']='ᶻ',
        ['0']='⁰',['1']='¹',['2']='²',['3']='³',['4']='⁴',['5']='⁵',['6']='⁶',['7']='⁷',['8']='⁸',['9']='⁹'
    };

    public static string ToTiny(string input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        var sb = new StringBuilder(input.Length * 2);
        foreach (var ch in input)
            sb.Append(TinyMap.TryGetValue(ch, out var t) ? t : ch);
        return sb.ToString();
    }

    public static SocketGuildUser? GetUserOption(SocketSlashCommand command, string name)
        => command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value as SocketGuildUser;

    public static string? GetStringOption(SocketSlashCommand command, string name)
        => command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value?.ToString();

    public static int GetIntOption(SocketSlashCommand command, string name, int fallback)
    {
        var opt = command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value;
        return opt switch
        {
            null => fallback,
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var v) => v,
            _ => fallback
        };
    }

    public static EmbedBuilder BasicEmbed(string title, string desc, Color? color = null)
        => new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(desc)
            .WithColor(color ?? new Color(0x5865F2))
            .WithCurrentTimestamp();

    public static bool HasAllowedRole(SocketGuildUser caller, string commandName)
    {
        var allow = TrollConfig.PerCommandAllow.TryGetValue(commandName, out var specific)
            ? specific
            : TrollConfig.AllowedRoleIds;
        return caller.Roles.Any(r => allow.Contains(r.Id));
    }

    public static async Task<bool> EnforceRoleGate(SocketSlashCommand command, string commandName)
    {
        if (command.User is not SocketGuildUser caller)
        {
            await command.RespondAsync("❌ This must be used in a server.", ephemeral: true);
            return false;
        }

        if (!HasAllowedRole(caller, commandName))
        {
            await command.RespondAsync("⛔ You don't have permission to use this command.", ephemeral: true);
            return false;
        }
        return true;
    }
}

#endregion

#region /rickroll

public class RickrollCommand : ISlashCommand
{
    public string Name => "rickroll";
    public string Description => "Totally not suspicious link for a friend.";

    private static readonly string[] Links = new[]
    {
        "https://youtu.be/dQw4w9WgXcQ",
        "https://www.youtube.com/watch?v=oHg5SJYRHA0"
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var target = TrollUtils.GetUserOption(command, "user");
        var link = TrollUtils.Pick(Links);

        var eb = TrollUtils.BasicEmbed("Totally useful tutorial", $"Hey {(target != null ? target.Mention : "there")}! Check this out: {link}")
            .WithFooter("100% not a rickroll");

        await command.RespondAsync(embed: eb.Build());
    }
}

#endregion

#region /fakeban

public class FakeBanCommand : ISlashCommand
{
    public string Name => "fakeban";
    public string Description => "Pretend to ban a user (harmless joke).";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var target = TrollUtils.GetUserOption(command, "user");
        var reason = (string)command.Data.Options.FirstOrDefault(x => x.Name == "reason")?.Value ?? "No reason provided";

        if (target == null)
        {
            await command.RespondAsync("❌ You must choose a user.", ephemeral: true);
            return;
        }

        var eb = TrollUtils.BasicEmbed("User Banned", 
                $"🔨 {target.Mention} has been **permanently banned** for: `{reason}`")
            .WithColor(Color.DarkRed)
            .WithFooter("This is a joke. No one was banned.");

        await command.RespondAsync(embed: eb.Build());
    }
}

#endregion

#region /screamer

public class ScreamerCommand : ISlashCommand
{
    public string Name => "screamer";
    public string Description => "Totally normal message that changes after a few seconds.";

    private static readonly string[] Gifs =
    {
        "https://media.tenor.com/4kB3iGmC8g4AAAAd/jumpscare.gif",
        "https://media.tenor.com/GXyQp1Nf0l0AAAAd/boo.gif"
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        await command.RespondAsync("This image is so calming… give it 3 seconds."); // no var

        await Task.Delay(3000);
        await command.ModifyOriginalResponseAsync(m =>
        {
            m.Content = "😱";
            m.Embed = new EmbedBuilder()
                .WithTitle("BOO!")
                .WithImageUrl(TrollUtils.Pick(Gifs))
                .WithColor(Color.DarkOrange)
                .Build();
        });
    }
}

#endregion

#region /reverse

public class ReverseCommand : ISlashCommand
{
    public string Name => "reverse";
    public string Description => "Reverse someone’s message.";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var text = TrollUtils.GetStringOption(command, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            await command.RespondAsync("❌ Provide some `text` to reverse.", ephemeral: true);
            return;
        }
        await command.RespondAsync(TrollUtils.ReverseString(text));
    }
}

#endregion

#region /tinytext

public class TinyTextCommand : ISlashCommand
{
    public string Name => "tinytext";
    public string Description => "Make text tiny and annoying (harmless).";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var text = TrollUtils.GetStringOption(command, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            await command.RespondAsync("❌ Provide some `text`.", ephemeral: true);
            return;
        }
        await command.RespondAsync(TrollUtils.ToTiny(text));
    }
}

#endregion

#region /lag

public class LagCommand : ISlashCommand
{
    public string Name => "lag";
    public string Description => "Pretend a user is lagging out.";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var target = TrollUtils.GetUserOption(command, "user")?.Mention ?? "User";
        var eb = TrollUtils.BasicEmbed("Connection Issue", $"🔌 `{target}` connection timed out.\n`Reason: ping 9999ms`")
            .WithColor(Color.LightGrey);

        await command.RespondAsync(embed: eb.Build());
    }
}

#endregion

#region /sus

public class SusCommand : ISlashCommand
{
    public string Name => "sus";
    public string Description => "Call someone sus with a meme.";

    private static readonly string[] SusGifs =
    {
        "https://media.tenor.com/5k6PNyZq7t8AAAAd/among-us-sus.gif",
        "https://media.tenor.com/zqzI2kT3y6wAAAAd/amongus-red.gif"
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var target = TrollUtils.GetUserOption(command, "user");
        var eb = TrollUtils.BasicEmbed("Sus Detected", $"{(target != null ? target.Mention : "Someone")} is kinda sus…")
            .WithImageUrl(TrollUtils.Pick(SusGifs))
            .WithColor(Color.DarkBlue);

        await command.RespondAsync(embed: eb.Build());
    }
}

#endregion

#region /hack

public class HackCommand : ISlashCommand
{
    public string Name => "hack";
    public string Description => "Pretend to hack a user with progress logs.";

    private static readonly string[] Steps =
    {
        "[░░░░░░░░░░] 5% - Stealing cookies…",
        "[██░░░░░░░░] 20% - Finding embarrassing photos…",
        "[████░░░░░░] 50% - Downloading memes…",
        "[██████░░░░] 70% - Cracking password: 123456",
        "[████████░░] 90% - Uploading to cloud…",
        "[██████████] 100% - Hack complete. Totally legit. ✅"
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var target = TrollUtils.GetUserOption(command, "user");
        await command.RespondAsync($"Initializing hack on {(target != null ? target.Mention : "target")}…"); // no var

        foreach (var s in Steps)
        {
            await Task.Delay(900);
            await command.ModifyOriginalResponseAsync(m => m.Content = s);
        }

        await command.ModifyOriginalResponseAsync(m =>
            m.Content += "\n*This is a joke. No data was touched.*");
    }
}

#endregion

#region /cursedimage

public class CursedImageCommand : ISlashCommand
{
    public string Name => "cursedimage";
    public string Description => "Send a random weird (SFW) image.";

    // Keep SFW and broadly safe.
    private static readonly string[] Images =
    {
        "https://i.imgur.com/ni3ddw6.png",
        "https://i.imgur.com/gMjTKlr.jpeg",
        "https://i.imgur.com/IaGILJW.jpeg",
        "https://i.imgur.com/7KNzj2X.jpeg"
    };

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var eb = TrollUtils.BasicEmbed("Behold…", "A deeply cursed (but safe) image appears!")
            .WithImageUrl(TrollUtils.Pick(Images));

        await command.RespondAsync(embed: eb.Build());
    }
}

#endregion

#region /loud

public class LoudCommand : ISlashCommand
{
    public string Name => "loud";
    public string Description => "Ping someone (visually) multiple times without real spam.";

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (!await TrollUtils.EnforceRoleGate(command, Name)) return;

        var target = TrollUtils.GetUserOption(command, "user") ?? command.User;
        var count = Math.Clamp(TrollUtils.GetIntOption(command, "count", 10), 1, 20);

        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(target.Mention);
        }

        // Shows as real mentions (blue), but **does not ping** anyone
        await command.RespondAsync(
            sb.ToString(),
            allowedMentions: AllowedMentions.None
        );
    }
}

#endregion
