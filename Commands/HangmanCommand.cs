using System.Text;
using Discord;
using Discord.WebSocket;

public class HangmanCommand : ISlashCommand
{
    public string Name => "hangman";
    public string Description => "Start a game of Hangman in this channel.";

    // ===== Permissions =====
    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355, // Game Mod
        1393623589122736238, // Discord Mod
        1393590761953558608  // SM
    };
    private bool HasPermission(SocketGuildUser u) => u.Roles.Any(r => allowedRoles.Contains(r.Id));

    // ===== Active Games =====
    private class Game
    {
        public string Word = "";
        public HashSet<char> Guessed = new();
        public HashSet<char> Wrong = new();
        public int Lives = 6;
        public ulong StarterId;
        public ulong ChannelId;
        public ulong MessageId;
    }
    private static readonly Dictionary<string, Game> Games = new();

    // ===== Word Bank =====
    private static readonly string[] Words =
    {
        // 🎮 Popular Games
        "MINECRAFT","FORTNITE","ROBLOX","OVERWATCH","APEX","VALORANT","CSGO","DOOM","HALO",
        "POKEMON","ZELDA","MARIO","SONIC","AMONGUS","TERRARIA","STARDEW","FIFA","NBA2K",
        "SKYRIM","FALLOUT","CYBERPUNK","WITCHER","PORTAL","LEFT4DEAD","TOMBRAIDER","BATMAN",
        "ARK","DAYZ","RUST","PUBG","DESTINY","WARZONE","CALLDUTY","LEAGUE","DOTA","WOW",
        "RUNESCAPE","OSRS","CLASH","CLASHROYAL","COC","BRAWLSTARS","ANIMALCROSSING","SMASH",
        "KIRBY","DONKEYKONG","METROID","RESIDENTEVIL","MONSTERHUNTER","MORTALKOMBAT","TEKKEN",
        "STREETFIGHTER","FIFA","ROCKETLEAGUE","GRANDFTHAUTO","REDDEAD","ELDENRING","SEKIRO",

        // 🌍 General Words
        "APPLE","BANANA","ORANGE","GRAPE","LEMON","MELON","PEAR","PEACH","CHERRY","MANGO",
        "BREAD","CHEESE","PIZZA","BURGER","SUSHI","TACO","NOODLE","SALAD","STEAK","FRIES",
        "DOG","CAT","MOUSE","HORSE","SHEEP","COW","DUCK","EAGLE","LION","TIGER","BEAR",
        "DRAGON","SHARK","FISH","SNAKE","FROG","RABBIT","WOLF","FOX","OWL","PANDA",
        "CAR","TRUCK","BUS","TRAIN","BOAT","SHIP","PLANE","BIKE","SCOOTER","SUBWAY",
        "CHAIR","TABLE","BED","DOOR","WINDOW","HOUSE","CASTLE","BRIDGE","TOWER","ROAD",
        "SUN","MOON","STAR","CLOUD","RAIN","SNOW","WIND","STORM","FIRE","EARTH","WATER",
        "BOOK","PEN","PENCIL","PAPER","BAG","SHOES","HAT","COAT","PHONE","LAPTOP","CLOCK",
        "GUITAR","DRUM","PIANO","VIOLIN","TRUMPET","FLUTE","MICROPHONE","RADIO","TV","CAMERA",
        "LOVE","HAPPY","SAD","ANGRY","LAUGH","CRY","SMILE","JUMP","RUN","WALK","SLEEP",
        "CITY","TOWN","VILLAGE","PARK","GARDEN","MOUNTAIN","RIVER","OCEAN","BEACH","FOREST",
        "GOLD","SILVER","BRONZE","DIAMOND","EMERALD","RUBY","PEARL","IRON","COPPER","STEEL",
        "KING","QUEEN","PRINCE","PRINCESS","KNIGHT","WIZARD","WITCH","ELF","ORC","GOBLIN",
        "SPACE","PLANET","ROCKET","ALIEN","ASTRONAUT","GALAXY","BLACKHOLE","MOONLIGHT","SUNLIGHT","UNIVERSE"
    };

    private static readonly string[] Gallows =
    {
        "```\n\n\n\n\n_____\n```",
        "```\n |\n |\n |\n |\n_|___\n```",
        "```\n ______\n |/\n |\n |\n |\n_|___\n```",
        "```\n ______\n |/   |\n |    O\n |\n |\n_|___\n```",
        "```\n ______\n |/   |\n |    O\n |    |\n |\n_|___\n```",
        "```\n ______\n |/   |\n |    O\n |   /|\\\n |\n_|___\n```",
        "```\n ______\n |/   |\n |    O\n |   /|\\\n |   / \\\n_|___\n```",
    };

    private static readonly Random Rng = new();

    // ===== Start Command =====
    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller || !HasPermission(caller))
        {
            await command.RespondAsync("⛔ You don’t have permission to start Hangman.", ephemeral: true);
            return;
        }

        var word = Words[Rng.Next(Words.Length)];
        var gameId = Guid.NewGuid().ToString("N");

        var game = new Game
        {
            Word = word,
            StarterId = command.User.Id,
            ChannelId = command.Channel.Id
        };
        Games[gameId] = game;

        var (embed, components) = BuildView(gameId, game, "🎲 Hangman — New Game");
        await command.RespondAsync(embed: embed, components: components, allowedMentions: AllowedMentions.None);
        var original = await command.GetOriginalResponseAsync();
        game.MessageId = original.Id;
    }

    // ===== Component Handling =====
    public static async Task HandleSelectAsync(SocketMessageComponent component)
    {
        // Expect: "hang:sel1:{gameId}" or "hang:sel2:{gameId}"
        if (!component.Data.CustomId.StartsWith("hang:sel")) return;

        var parts = component.Data.CustomId.Split(':'); // hang sel1 gameId
        if (parts.Length != 3) return;
        var gameId = parts[2];

        if (!Games.TryGetValue(gameId, out var game))
        {
            await component.RespondAsync("This game has ended.", ephemeral: true);
            return;
        }

        var letterStr = component.Data.Values?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(letterStr)) { await component.RespondAsync("Pick a letter!", ephemeral: true); return; }

        var ch = char.ToUpperInvariant(letterStr[0]);

        if (game.Guessed.Contains(ch) || game.Wrong.Contains(ch))
        {
            await component.RespondAsync("Already guessed that letter.", ephemeral: true);
            return;
        }

        if (game.Word.Contains(ch)) game.Guessed.Add(ch);
        else { game.Wrong.Add(ch); game.Lives = Math.Max(0, game.Lives - 1); }

        var won = IsWin(game);
        var lost = game.Lives <= 0;

        var (embed, components) = BuildView(gameId, game, won ? "✅ You Win!" : lost ? "❌ You Lose!" : "🎯 Hangman");

        await component.UpdateAsync(msg =>
        {
            msg.Embed = embed;
            msg.Components = components;
            msg.AllowedMentions = AllowedMentions.None;
        });

        if (won || lost) Games.Remove(gameId);
    }

    // ===== Helpers =====
    private static (Embed, MessageComponent) BuildView(string gameId, Game g, string title)
{
    string Mask(string w, HashSet<char> guessed)
        => string.Join(' ', w.Select(ch => ch == ' ' ? ' ' : (guessed.Contains(ch) ? ch : '_'))).TrimEnd();

    var masked = Mask(g.Word, g.Guessed);
    var wrong = g.Wrong.Count == 0 ? "—" : string.Join(" ", g.Wrong.OrderBy(c => c));
    var stage = Gallows[6 - g.Lives];

    var desc = (IsWin(g), g.Lives <= 0) switch
    {
        (true, _)  => $"`{g.Word}`\n\n**You nailed it!** 🎉",
        (_, true)  => $"The word was: **`{g.Word}`**\n\nBetter luck next time!",
        _          => $"{stage}\n**Word:** `{masked}`\n**Lives:** {new string('❤', g.Lives)}  ({g.Lives}/6)\n**Wrong:** `{wrong}`\n\nPick a letter from the menus below."
    };

    var eb = new EmbedBuilder()
        .WithTitle(title)
        .WithDescription(desc)
        .WithColor(IsWin(g) ? new Color(0x22BB66) : (g.Lives <= 0 ? new Color(0xCC3333) : new Color(0x5865F2)))
        .WithFooter($"Started by: {MentionUtils.MentionUser(g.StarterId)} • Game #{gameId[..6].ToUpperInvariant()}");

    bool disabled = IsWin(g) || g.Lives <= 0;

    var remaining = Enumerable.Range('A', 26).Select(i => (char)i)
        .Except(g.Guessed)
        .Except(g.Wrong)
        .ToHashSet();

    SelectMenuBuilder BuildMenu(string id, IEnumerable<char> letters, string placeholder)
    {
        var m = new SelectMenuBuilder()
            .WithCustomId(id)
            .WithPlaceholder(placeholder)
            .WithMinValues(1).WithMaxValues(1)
            .WithDisabled(disabled);

        foreach (var c in letters)
            m.AddOption(c.ToString(), c.ToString());

        if (m.Options.Count == 0)
        {
            m.AddOption("No letters left", "none", isDefault: true);
            m.IsDisabled = true;
        }
        return m;
    }

    var group1 = remaining.Where(c => c <= 'M'); // 13 max
    var group2 = remaining.Where(c => c >= 'N'); // 13 max (<=14 including N..Z but under 25 limit anyway)

    var comps = new ComponentBuilder()
        .WithSelectMenu(BuildMenu($"hang:sel1:{gameId}", group1, "Letters A–M"))
        .WithSelectMenu(BuildMenu($"hang:sel2:{gameId}", group2, "Letters N–Z"))
        .Build();

    return (eb.Build(), comps);
}

    private static bool IsWin(Game g) => g.Word.All(ch => ch == ' ' || g.Guessed.Contains(ch));
}
