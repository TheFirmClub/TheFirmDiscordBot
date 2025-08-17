using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class HangmanCommand : ISlashCommand
{
    public string Name => "hangman";
    public string Description => "Start a game of Hangman in this channel.";

    // ===== Permissions (same as your Trivia) =====
    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355, // Game Mod
        1393623589122736238, // Discord Mod
        1393590761953558608  // SM
    };
    private bool HasPermission(SocketGuildUser u) => u.Roles.Any(r => allowedRoles.Contains(r.Id));

    // ===== State =====
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

    // ===== Words / Art =====
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

    // ===== Slash entry =====
    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller || !HasPermission(caller))
        {
            await command.RespondAsync("⛔ You don’t have permission to start Hangman.", ephemeral: true);
            return;
        }

        var gameId = Guid.NewGuid().ToString("N");
        var word = Words[Rng.Next(Words.Length)];

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

    // ===== Button handler (wire in Program.cs) =====
    public static async Task HandleButtonAsync(SocketMessageComponent component)
    {
        // Expect: hang:btn:{gameId}:{label}  (label = "A".."X" or "YZ")
        if (!component.Data.CustomId.StartsWith("hang:btn:")) return;

        var parts = component.Data.CustomId.Split(':'); // hang btn gameId label
        if (parts.Length != 4) return;

        var gameId = parts[2];
        var label = parts[3]; // "A".."X" or "YZ"

        if (!Games.TryGetValue(gameId, out var game))
        {
            await component.RespondAsync("This game has ended.", ephemeral: true);
            return;
        }

        // Convert label to letters (YZ is two at once)
        var letters = label == "YZ"
            ? new[] { 'Y', 'Z' }
            : new[] { char.ToUpperInvariant(label[0]) };

        // If all letters already chosen, just ignore
        if (letters.All(ch => game.Guessed.Contains(ch) || game.Wrong.Contains(ch)))
        {
            await component.RespondAsync("Already picked.", ephemeral: true);
            return;
        }

        bool anyHit = false;
        foreach (var ch in letters)
        {
            if (game.Word.Contains(ch))
            {
                anyHit = true;
                game.Guessed.Add(ch);
            }
        }

        if (!anyHit)
        {
            // Count as one wrong guess even if label has two letters (YZ)
            // Only add letters to Wrong that weren’t seen yet
            foreach (var ch in letters)
                if (!game.Guessed.Contains(ch)) game.Wrong.Add(ch);
            game.Lives = Math.Max(0, game.Lives - 1);
        }

        var won  = IsWin(game);
        var lost = game.Lives <= 0;

        var (embed, comps) = BuildView(gameId, game, won ? "✅ You Win!" : (lost ? "❌ You Lose!" : "🎯 Hangman"));

        await component.UpdateAsync(m =>
        {
            m.Embed = embed;
            m.Components = comps;
            m.AllowedMentions = AllowedMentions.None;
        });

        if (won || lost) Games.Remove(gameId);
    }

    // ===== Rendering =====
    private static (Embed, MessageComponent) BuildView(string gameId, Game g, string title)
    {
        string Mask(string w, HashSet<char> guessed)
            => string.Join(' ', w.Select(ch => ch == ' ' ? ' ' : (guessed.Contains(ch) ? ch : '_'))).TrimEnd();

        var masked = Mask(g.Word, g.Guessed);
        var wrong  = g.Wrong.Count == 0 ? "—" : string.Join(" ", g.Wrong.OrderBy(c => c));
        var stage  = Gallows[6 - g.Lives];

        string desc;
        if (IsWin(g))
            desc = $"`{g.Word}`\n\n**You nailed it!** 🎉";
        else if (g.Lives <= 0)
            desc = $"The word was: **`{g.Word}`**\n\nBetter luck next time!";
        else
            desc = $"{stage}\n**Word:** `{masked}`\n**Lives:** {new string('❤', g.Lives)}  ({g.Lives}/6)\n**Wrong:** `{wrong}`\n\nPress a letter below.";

        var eb = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(desc)
            .WithColor(IsWin(g) ? new Color(0x22BB66) : (g.Lives <= 0 ? new Color(0xCC3333) : new Color(0x5865F2)))
            .WithFooter($"Started by: {MentionUtils.MentionUser(g.StarterId)} • Game #{gameId[..6].ToUpperInvariant()}");

        bool disabled = IsWin(g) || g.Lives <= 0;

        // 25 buttons, 5 rows x 5 columns: A..X then "YZ"
        var labels = new[]
        {
            "A","B","C","D","E","F","G","H","I","J",
            "K","L","M","N","O","P","Q","R","S","T",
            "U","V","W","X","YZ"
        };

        var cb = new ComponentBuilder();
        for (int row = 0; row < 5; row++)
        {
            var rowLabels = labels.Skip(row * 5).Take(5);
            var actionRow = new ActionRowBuilder();

            foreach (var label in rowLabels)
            {
                var letters = label == "YZ" ? new[] { 'Y', 'Z' } : new[] { label[0] };
                bool already = letters.All(ch => g.Guessed.Contains(ch) || g.Wrong.Contains(ch));

                var btn = new ButtonBuilder()
                    .WithLabel(label)
                    .WithCustomId($"hang:btn:{gameId}:{label}")
                    .WithStyle(ButtonStyle.Primary)
                    .WithDisabled(disabled || already);

                actionRow.AddComponent(btn.Build());
            }

            cb.AddRow(actionRow);
        }

        return (eb.Build(), cb.Build());
    }

    private static bool IsWin(Game g) => g.Word.All(ch => ch == ' ' || g.Guessed.Contains(ch));
}
