using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class TriviaCommand : ISlashCommand
{
    public string Name => "trivia";
    public string Description => "Start a quick multiple-choice trivia round.";

    // ===== Active round state (per channel) =====
    private static readonly Dictionary<ulong, State> Active = new();

    private record State(
        ulong MessageId,
        ulong ChannelId,
        DateTimeOffset Deadline,
        int CorrectIndex,
        string[] Options,
        string Prompt,
        string Category,
        bool Finished
    );

    // ===== Permissions =====
    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355,
        1393623589122736238,
        1393590761953558608
    };

    private bool HasPermission(SocketGuildUser u) => u.Roles.Any(r => allowedRoles.Contains(r.Id));

    // ===== No-repeat decks (per channel + category) =====
    private class Deck
    {
        public Queue<int> Order = new();
        public Queue<int> Recent = new(); // sliding window of recently used indexes (within pool)
        public DateTimeOffset LastReset = DateTimeOffset.MinValue;
    }

    // Keyed by $"{channelId}:{categoryOrALL}"
    private static readonly Dictionary<string, Deck> Decks = new();

    private const int RecentWindowSize = 20; // how many recent Qs to avoid
    private static readonly TimeSpan DeckResetInterval = TimeSpan.FromHours(1); // periodic reset
    private static readonly Random Rng = new();

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void RebuildDeck(Deck deck, int poolSize)
    {
        var indices = Enumerable.Range(0, poolSize).ToList();
        Shuffle(indices);
        deck.Order.Clear();
        foreach (var i in indices) deck.Order.Enqueue(i);
        deck.LastReset = DateTimeOffset.UtcNow;
    }

    private static int GetNextQuestionIndex(Deck deck, int poolSize)
    {
        int safety = poolSize + 10;
        while (safety-- > 0)
        {
            if (deck.Order.Count == 0) RebuildDeck(deck, poolSize);
            var idx = deck.Order.Dequeue();

            if (!deck.Recent.Contains(idx))
            {
                deck.Recent.Enqueue(idx);
                while (deck.Recent.Count > RecentWindowSize)
                    deck.Recent.Dequeue();
                return idx;
            }

            // recently seen — push back for later
            deck.Order.Enqueue(idx);
        }

        // Fallback (very rare)
        var fallback = Rng.Next(poolSize);
        deck.Recent.Enqueue(fallback);
        while (deck.Recent.Count > RecentWindowSize)
            deck.Recent.Dequeue();
        return fallback;
    }

    private static Question PickQuestion(string? category, ulong channelId)
    {
        var pool = string.IsNullOrWhiteSpace(category)
            ? Bank
            : Bank.Where(q => q.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

        if (pool.Count == 0) pool = Bank;

        var deckKey = $"{channelId}:{(string.IsNullOrWhiteSpace(category) ? "ALL" : category)}";
        if (!Decks.TryGetValue(deckKey, out var deck))
        {
            deck = new Deck();
            Decks[deckKey] = deck;
            RebuildDeck(deck, pool.Count);
        }

        if (DateTimeOffset.UtcNow - deck.LastReset > DeckResetInterval || deck.Order.Count == 0)
            RebuildDeck(deck, pool.Count);

        int idxInPool = GetNextQuestionIndex(deck, pool.Count);
        return pool[idxInPool];
    }

    // ===== Public API =====
    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ Use this in a server.", ephemeral: true);
            return;
        }

        if (!HasPermission(user))
        {
            await command.RespondAsync("❌ You don’t have permission.", ephemeral: true);
            return;
        }

        var channelId = command.ChannelId!.Value;
        if (Active.ContainsKey(channelId))
        {
            await command.RespondAsync("⚠️ There’s already a trivia running in this channel.", ephemeral: true);
            return;
        }

        // optional category
        string? category = command.Data.Options
            .FirstOrDefault(o => o.Name.Equals("category", StringComparison.OrdinalIgnoreCase))
            ?.Value as string;

        var q = PickQuestion(category, channelId);

        // Build options (A-D) with shuffled order
        var opts = new List<(string text, bool isCorrect)>
        {
            (q.Correct, true),
            (q.Wrong[0], false),
            (q.Wrong[1], false),
            (q.Wrong[2], false),
        };
        Shuffle(opts);
        var optionTexts = opts.Select(o => o.text).ToArray();
        var correctIndex = Array.FindIndex(opts.ToArray(), o => o.isCorrect);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        var labels = new[] { "A", "B", "C", "D" };
        var embed = new EmbedBuilder()
            .WithTitle($"🧠 Trivia — {q.Category}")
            .WithDescription(
                $"**{q.Prompt}**\n\n" +
                string.Join("\n", optionTexts.Select((t, i) => $"**{labels[i]}.** {t}")) +
                $"\n\n⏳ You have **30s**. First correct click wins!")
            .WithColor(Color.Blue)
            .WithFooter($"Ends ~ {deadline:HH:mm:ss} UTC")
            .Build();

        // Send initial with pending IDs, then stamp actual messageId
        await command.RespondAsync(embed: embed, components: BuildButtons("pending", optionTexts.Length));
        var msg = await command.GetOriginalResponseAsync();

        Active[channelId] = new State(
            MessageId: msg.Id,
            ChannelId: msg.Channel.Id,
            Deadline: deadline,
            CorrectIndex: correctIndex,
            Options: optionTexts,
            Prompt: q.Prompt,
            Category: q.Category,
            Finished: false
        );

        await msg.ModifyAsync(m => m.Components = BuildButtons(msg.Id.ToString(), optionTexts.Length));

        // Timeout task
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(31));
            if (Active.TryGetValue(channelId, out var s) && s.MessageId == msg.Id && !s.Finished)
            {
                Active.Remove(channelId);
                var expired = new EmbedBuilder()
                    .WithTitle($"🧠 Trivia — {s.Category}")
                    .WithDescription(
                        $"**{s.Prompt}**\n\n⏰ Time’s up! No winner this round.\n\n**Answer:** {s.Options[s.CorrectIndex]}")
                    .WithColor(Color.DarkGrey)
                    .Build();

                try
                {
                    await msg.ModifyAsync(mm =>
                    {
                        mm.Embed = expired;
                        mm.Components = DisableButtons(s.MessageId.ToString(), s.Options.Length);
                    });
                }
                catch
                {
                    /* message may be deleted; ignore */
                }
            }
        });
    }

    // Call from your global ButtonExecuted
    public static async Task HandleButton(SocketMessageComponent component)
    {
        if (component.Data.CustomId is null || !component.Data.CustomId.StartsWith("trivia:"))
            return;

        // trivia:pick:<messageId>:<index>
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 4 || parts[1] != "pick") return;
        if (!ulong.TryParse(parts[2], out var msgId)) return;
        if (!int.TryParse(parts[3], out var pickIndex)) return;

        var channelId = component.Channel.Id;
        if (!Active.TryGetValue(channelId, out var state) || state.MessageId != msgId)
        {
            await component.RespondAsync("This trivia has ended.", ephemeral: true);
            return;
        }

        if (state.Finished || DateTimeOffset.UtcNow > state.Deadline)
        {
            Active.Remove(channelId);
            await component.RespondAsync("⏰ This trivia round has already ended.", ephemeral: true);
            try
            {
                await component.Message.ModifyAsync(m =>
                {
                    m.Components = DisableButtons(msgId.ToString(), state.Options.Length);
                });
            }
            catch
            {
            }

            return;
        }

        var labels = new[] { "A", "B", "C", "D" };

        if (pickIndex == state.CorrectIndex)
        {
            Active.Remove(channelId);
            var winner = component.User;

            var win = new EmbedBuilder()
                .WithTitle($"🧠 Trivia — {state.Category}")
                .WithDescription(
                    $"**{state.Prompt}**\n\n" +
                    string.Join("\n", state.Options.Select((t, i) =>
                        i == state.CorrectIndex ? $"**{labels[i]}.** ✅ **{t}**" : $"**{labels[i]}.** {t}")) +
                    $"\n\n🏆 Winner: {winner.Mention}")
                .WithColor(Color.Green)
                .Build();

            await component.UpdateAsync(m =>
            {
                m.Embed = win;
                m.Components = DisableButtons(msgId.ToString(), state.Options.Length);
            });
        }
        else
        {
            await component.RespondAsync("❌ Wrong — keep trying!", ephemeral: true);
        }
    }

    // ===== UI helpers =====
    private static MessageComponent BuildButtons(string messageId, int count)
    {
        var labels = new[] { "A", "B", "C", "D" };
        var builder = new ComponentBuilder();
        var row = new ActionRowBuilder();
        for (int i = 0; i < count && i < 4; i++)
            row.WithButton(labels[i], $"trivia:pick:{messageId}:{i}", ButtonStyle.Primary);
        builder.AddRow(row);
        return builder.Build();
    }

    private static MessageComponent DisableButtons(string messageId, int count)
    {
        var labels = new[] { "A", "B", "C", "D" };
        var builder = new ComponentBuilder();
        var row = new ActionRowBuilder();
        for (int i = 0; i < count && i < 4; i++)
            row.WithButton(labels[i], $"trivia:off:{messageId}:{i}", ButtonStyle.Secondary, disabled: true);
        builder.AddRow(row);
        return builder.Build();
    }

    // ===== Question model + BANK =====
    public record Question(string Category, string Prompt, string Correct, string[] Wrong);
    
    private static readonly List<Question> Bank = new()
    {
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Rosalina", "Princess Daisy", "Pauline" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Fusilli", "Spaghetti", "Penne" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Total Recall", "Predator" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Fusilli", "Penne", "Spaghetti" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Microsoft", "Sega" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Steve Jobs", "Mark Zuckerberg" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "John Adams", "Benjamin Franklin", "Thomas Jefferson" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Louis Pasteur", "Marie Curie" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Princess Daisy", "Pauline", "Rosalina" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Fermentation", "Respiration", "Transpiration" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Avengers: Endgame", "Star Wars: The Force Awakens", "Titanic" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Nintendo", "Microsoft" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "Hyperlink Transfer Program", "HighText Transfer Procedure" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HighText Transfer Procedure", "Hyperlink Transfer Program", "HyperText Transmission Protocol" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "11", "13" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Penne", "Spaghetti", "Fusilli" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Sega", "Microsoft" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "White Rhino", "Giraffe", "Hippopotamus" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Apple", "Strawberry", "Banana" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Mississippi", "Amazon", "Yangtze" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Predator", "Total Recall", "Commando" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Titanic", "Avengers: Endgame" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Hippopotamus", "Giraffe", "White Rhino" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "John Adams", "Thomas Jefferson", "Benjamin Franklin" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Predator", "Total Recall" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Fusilli", "Penne" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Microsoft", "Sega", "Nintendo" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Banana", "Strawberry", "Apple" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Mark Zuckerberg", "Steve Jobs", "Larry Page" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Strawberry", "Banana", "Apple" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Steve Jobs", "Larry Page", "Mark Zuckerberg" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Prince", "Freddie Mercury" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Mercury", "Venus", "Jupiter" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "John Adams", "Thomas Jefferson", "Benjamin Franklin" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Fusilli", "Penne" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Tomato", "Peas" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Peas", "Tomato" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Freddie Mercury", "Prince" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Penne", "Fusilli", "Spaghetti" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Steve Jobs", "Mark Zuckerberg" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "John Adams", "Benjamin Franklin", "Thomas Jefferson" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Brad Pitt", "Tom Cruise" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Total Recall", "Predator" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "China", "Vietnam", "Thailand" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Rosalina", "Princess Daisy" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Steve Jobs", "Mark Zuckerberg" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Pb", "Ag" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Transpiration", "Respiration", "Fermentation" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Peas", "Tomato" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HighText Transfer Procedure", "HyperText Transmission Protocol", "Hyperlink Transfer Program" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "China", "Vietnam", "Thailand" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Predator", "Total Recall", "Commando" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Pb", "Ag", "Gd" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "Hyperlink Transfer Program", "HighText Transfer Procedure" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Mark Zuckerberg", "Steve Jobs", "Larry Page" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "11", "13", "9" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Central Program Utility", "Computer Personal Unit" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Fermentation", "Transpiration", "Respiration" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "Thailand", "China" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Brisbane", "Melbourne", "Sydney" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Rosalina", "Princess Daisy", "Pauline" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Brazil", "Croatia", "Germany" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Joseph Lister", "Louis Pasteur" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Yangtze", "Mississippi", "Amazon" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1987", "1991", "1993" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Strawberry", "Apple", "Banana" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1991", "1987", "1993" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Freddie Mercury", "Prince", "Elvis Presley" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "Thailand", "China" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Marie Curie", "Louis Pasteur" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Tom Cruise", "Brad Pitt" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Strawberry", "Banana", "Apple" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Cucumber", "Tomato" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Benjamin Franklin", "John Adams", "Thomas Jefferson" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Helium", "Oxygen", "Nitrogen" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "HighText Transfer Procedure", "Hyperlink Transfer Program" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Total Recall", "Predator" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Transpiration", "Fermentation", "Respiration" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Prince", "Freddie Mercury" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Banana", "Apple", "Strawberry" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Marie Curie", "Louis Pasteur" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Ag", "Pb" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Penne", "Fusilli" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Microsoft", "Sega" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Titanic", "Avengers: Endgame" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Arctic Ocean", "Indian Ocean" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Thailand", "China", "Vietnam" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Fermentation", "Transpiration", "Respiration" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Titanic", "Avengers: Endgame" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Apple", "Strawberry", "Banana" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Beyoncé", "Rihanna", "Taylor Swift" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Steve Jobs", "Mark Zuckerberg", "Larry Page" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Brazil", "Croatia" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Brazil", "Germany", "Croatia" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Penne", "Spaghetti", "Fusilli" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Marie Curie", "Louis Pasteur" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Central Program Utility", "Control Processing Unit", "Computer Personal Unit" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Brazil", "Croatia" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Rihanna", "Beyoncé", "Taylor Swift" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Amazon", "Yangtze", "Mississippi" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Croatia", "Brazil", "Germany" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Brad Pitt", "Tom Cruise" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Louis Pasteur", "Joseph Lister" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Thomas Jefferson", "Benjamin Franklin", "John Adams" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Microsoft", "Nintendo", "Sega" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "Hyperlink Transfer Program", "HyperText Transmission Protocol", "HighText Transfer Procedure" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Pb", "Ag" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Croatia", "Brazil" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Strawberry", "Banana", "Apple" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Rosalina", "Princess Daisy" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "End Stone", "Stone" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Brad Pitt", "Matt Damon", "Tom Cruise" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Oxygen", "Nitrogen", "Helium" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Fusilli", "Spaghetti", "Penne" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Crocodile", "Turtle", "Snake" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "11", "9", "13" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Marie Curie", "Louis Pasteur" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "White Rhino", "Giraffe", "Hippopotamus" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Computer Personal Unit", "Central Program Utility" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Snake", "Turtle", "Crocodile" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Taylor Swift", "Rihanna", "Beyoncé" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "11", "13", "9" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1991", "1987", "1993" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "13", "11" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Giraffe", "White Rhino", "Hippopotamus" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Avengers: Endgame", "Star Wars: The Force Awakens", "Titanic" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Central Program Utility", "Computer Personal Unit" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Amazon", "Yangtze", "Mississippi" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Indian Ocean", "Arctic Ocean", "Atlantic Ocean" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Mississippi", "Amazon", "Yangtze" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Total Recall", "Predator" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Mississippi", "Yangtze", "Amazon" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Mark Zuckerberg", "Steve Jobs" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "Stone", "End Stone" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Microsoft", "Nintendo" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Pink Floyd", "Queen", "The Rolling Stones" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Pink Floyd", "Queen", "The Rolling Stones" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Prince", "Elvis Presley", "Freddie Mercury" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Avengers: Endgame", "Star Wars: The Force Awakens", "Titanic" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1987", "1993", "1991" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Princess Daisy", "Rosalina" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Brad Pitt", "Matt Damon" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "HighText Transfer Procedure", "Hyperlink Transfer Program" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Stone", "End Stone", "Netherrack" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Oxygen", "Helium", "Nitrogen" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1993", "1987", "1991" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Amazon", "Yangtze", "Mississippi" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Pink Floyd", "The Rolling Stones", "Queen" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Indian Ocean", "Arctic Ocean" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Hippopotamus", "White Rhino", "Giraffe" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Fusilli", "Spaghetti", "Penne" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Rosalina", "Princess Daisy" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Central Program Utility", "Computer Personal Unit" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Princess Daisy", "Rosalina" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Predator", "Total Recall" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Sydney", "Melbourne", "Brisbane" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Princess Daisy", "Pauline", "Rosalina" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Turtle", "Snake", "Crocodile" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Indian Ocean", "Atlantic Ocean", "Arctic Ocean" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Turtle", "Crocodile", "Snake" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "11", "9" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Thailand", "Vietnam", "China" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "Stone", "End Stone" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1991", "1993", "1987" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Respiration", "Fermentation", "Transpiration" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Venus", "Mercury", "Jupiter" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "Stone", "End Stone" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Brad Pitt", "Matt Damon" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Tom Cruise", "Brad Pitt" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Giraffe", "Hippopotamus", "White Rhino" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Rosalina", "Pauline", "Princess Daisy" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Tom Cruise", "Brad Pitt" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "11", "13" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Brazil", "Croatia" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Control Processing Unit", "Central Program Utility" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Avengers: Endgame", "Titanic" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Pb", "Ag" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "China", "Thailand" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Fusilli", "Penne" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Indian Ocean", "Arctic Ocean" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Fermentation", "Transpiration", "Respiration" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Tomato", "Cucumber", "Peas" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Sydney", "Melbourne", "Brisbane" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Mississippi", "Amazon", "Yangtze" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "Hyperlink Transfer Program", "HyperText Transmission Protocol", "HighText Transfer Procedure" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Microsoft", "Sega" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Jupiter", "Mercury", "Venus" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Computer Personal Unit", "Central Program Utility" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Taylor Swift", "Rihanna", "Beyoncé" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Fusilli", "Spaghetti", "Penne" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Titanic", "Avengers: Endgame", "Star Wars: The Force Awakens" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Marie Curie", "Joseph Lister" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Pink Floyd", "Queen", "The Rolling Stones" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "Thailand", "China" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Mark Zuckerberg", "Larry Page", "Steve Jobs" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Tomato", "Cucumber", "Peas" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "Hyperlink Transfer Program", "HighText Transfer Procedure" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "John Adams", "Thomas Jefferson", "Benjamin Franklin" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Microsoft", "Sega" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Brad Pitt", "Matt Damon" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Nitrogen", "Oxygen", "Helium" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HighText Transfer Procedure", "HyperText Transmission Protocol", "Hyperlink Transfer Program" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Beyoncé", "Taylor Swift", "Rihanna" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "11", "9" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Computer Personal Unit", "Central Program Utility" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Control Processing Unit", "Central Program Utility" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Hippopotamus", "White Rhino", "Giraffe" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Ag", "Pb", "Gd" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Tomato", "Peas", "Cucumber" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HighText Transfer Procedure", "HyperText Transmission Protocol", "Hyperlink Transfer Program" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "11", "13" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Turtle", "Crocodile", "Snake" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Louis Pasteur", "Marie Curie" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Helium", "Oxygen", "Nitrogen" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Pink Floyd", "Queen", "The Rolling Stones" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Pink Floyd", "Queen", "The Rolling Stones" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Avengers: Endgame", "Titanic", "Star Wars: The Force Awakens" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Steve Jobs", "Larry Page", "Mark Zuckerberg" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "11", "13" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Taylor Swift", "Beyoncé", "Rihanna" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Freddie Mercury", "Prince", "Elvis Presley" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Joseph Lister", "Marie Curie" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Tom Cruise", "Brad Pitt" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Hippopotamus", "Giraffe", "White Rhino" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Marie Curie", "Louis Pasteur" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Fusilli", "Penne" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Taylor Swift", "Rihanna", "Beyoncé" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1993", "1987", "1991" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "China", "Vietnam", "Thailand" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Prince", "Freddie Mercury" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Brazil", "Croatia", "Germany" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Benjamin Franklin", "Thomas Jefferson", "John Adams" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Pb", "Gd", "Ag" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Titanic", "Avengers: Endgame" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "11", "9", "13" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Melbourne", "Sydney", "Brisbane" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Giraffe", "White Rhino", "Hippopotamus" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Peas", "Tomato" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HighText Transfer Procedure", "HyperText Transmission Protocol", "Hyperlink Transfer Program" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Snake", "Crocodile", "Turtle" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Avengers: Endgame", "Titanic" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Microsoft", "Nintendo" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "White Rhino", "Giraffe", "Hippopotamus" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Apple", "Strawberry", "Banana" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Microsoft", "Nintendo" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Jupiter", "Mercury", "Venus" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Brad Pitt", "Matt Damon", "Tom Cruise" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1993", "1987", "1991" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Computer Personal Unit", "Central Program Utility" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Marie Curie", "Joseph Lister" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Indian Ocean", "Atlantic Ocean", "Arctic Ocean" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Tomato", "Peas" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Mercury", "Venus", "Jupiter" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Microsoft", "Nintendo" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Helium", "Nitrogen", "Oxygen" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Microsoft", "Sega" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Sydney", "Melbourne", "Brisbane" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Hippopotamus", "Giraffe", "White Rhino" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Taylor Swift", "Beyoncé", "Rihanna" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "End Stone", "Stone" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Freddie Mercury", "Elvis Presley", "Prince" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Titanic", "Star Wars: The Force Awakens", "Avengers: Endgame" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Venus", "Jupiter", "Mercury" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Brisbane", "Sydney", "Melbourne" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Brazil", "Croatia", "Germany" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Peas", "Tomato" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Peas", "Tomato" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "China", "Thailand", "Vietnam" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Tom Cruise", "Brad Pitt" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Oxygen", "Nitrogen", "Helium" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Nitrogen", "Oxygen", "Helium" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Nintendo", "Sega", "Microsoft" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Microsoft", "Sega", "Nintendo" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Ag", "Gd", "Pb" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Croatia", "Brazil", "Germany" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Mercury", "Jupiter", "Venus" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Apple", "Banana", "Strawberry" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Beyoncé", "Rihanna", "Taylor Swift" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Brazil", "Croatia", "Germany" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Control Processing Unit", "Central Program Utility" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1987", "1991", "1993" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Strawberry", "Banana", "Apple" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "11", "13", "9" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Arctic Ocean", "Indian Ocean" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Cucumber", "Tomato", "Peas" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Louis Pasteur", "Joseph Lister" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Penne", "Fusilli" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Commando", "Predator", "Total Recall" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Pb", "Ag", "Gd" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Prince", "Freddie Mercury", "Elvis Presley" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Joseph Lister", "Marie Curie", "Louis Pasteur" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Jupiter", "Mercury", "Venus" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Helium", "Nitrogen", "Oxygen" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Sydney", "Melbourne", "Brisbane" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Predator", "Total Recall", "Commando" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "End Stone", "Stone" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Prince", "Freddie Mercury" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Yangtze", "Amazon", "Mississippi" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1991", "1987", "1993" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Rihanna", "Taylor Swift", "Beyoncé" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Control Processing Unit", "Central Program Utility" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "13", "11" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Snake", "Crocodile", "Turtle" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Taylor Swift", "Rihanna", "Beyoncé" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Stone", "End Stone", "Netherrack" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Central Program Utility", "Control Processing Unit", "Computer Personal Unit" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Arctic Ocean", "Atlantic Ocean", "Indian Ocean" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Brazil", "Germany", "Croatia" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Brisbane", "Melbourne", "Sydney" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Snake", "Crocodile", "Turtle" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Banana", "Apple", "Strawberry" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Pb", "Ag" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Rosalina", "Pauline", "Princess Daisy" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Nintendo", "Microsoft" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "The Rolling Stones", "Pink Floyd" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Penne", "Fusilli" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Ag", "Pb", "Gd" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Thomas Jefferson", "John Adams", "Benjamin Franklin" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "9", "11" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Respiration", "Fermentation", "Transpiration" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Avengers: Endgame", "Titanic" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Rihanna", "Taylor Swift", "Beyoncé" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "China", "Thailand" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "The Rolling Stones", "Queen", "Pink Floyd" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Arctic Ocean", "Indian Ocean" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Central Program Utility", "Control Processing Unit" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Ag", "Pb" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Rihanna", "Taylor Swift", "Beyoncé" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Cucumber", "Tomato" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Mark Zuckerberg", "Steve Jobs" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Louis Pasteur", "Joseph Lister" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "Hyperlink Transfer Program", "HyperText Transmission Protocol", "HighText Transfer Procedure" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Crocodile", "Turtle", "Snake" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Matt Damon", "Tom Cruise", "Brad Pitt" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Princess Daisy", "Pauline", "Rosalina" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Cucumber", "Tomato" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Titanic", "Star Wars: The Force Awakens", "Avengers: Endgame" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Central Program Utility", "Control Processing Unit" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "The Rolling Stones", "Pink Floyd", "Queen" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Croatia", "Germany", "Brazil" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Brisbane", "Melbourne", "Sydney" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Thomas Jefferson", "John Adams", "Benjamin Franklin" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1987", "1991", "1993" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Turtle", "Snake", "Crocodile" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Jupiter", "Venus", "Mercury" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Stone", "Netherrack", "End Stone" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Thailand", "China", "Vietnam" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Penne", "Spaghetti", "Fusilli" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Mississippi", "Yangtze", "Amazon" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Tomato", "Cucumber", "Peas" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Crocodile", "Turtle", "Snake" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "White Rhino", "Giraffe", "Hippopotamus" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Yangtze", "Amazon", "Mississippi" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Rihanna", "Beyoncé", "Taylor Swift" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "The Rolling Stones", "Pink Floyd" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Mississippi", "Yangtze", "Amazon" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Matt Damon", "Brad Pitt" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1991", "1987", "1993" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "Stone", "End Stone" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Venus", "Jupiter", "Mercury" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Freddie Mercury", "Elvis Presley", "Prince" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Oxygen", "Nitrogen", "Helium" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Banana", "Apple", "Strawberry" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "HighText Transfer Procedure", "Hyperlink Transfer Program" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "11", "9" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Princess Daisy", "Rosalina" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Joseph Lister", "Marie Curie" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Avengers: Endgame", "Star Wars: The Force Awakens", "Titanic" }),
        new("Technology", "What does 'HTTP' stand for?", "HyperText Transfer Protocol",
            new[] { "HyperText Transmission Protocol", "Hyperlink Transfer Program", "HighText Transfer Procedure" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Benjamin Franklin", "Thomas Jefferson", "John Adams" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Princess Daisy", "Rosalina", "Pauline" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Banana", "Apple", "Strawberry" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Titanic", "Avengers: Endgame", "Star Wars: The Force Awakens" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Ag", "Pb" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Rihanna", "Taylor Swift", "Beyoncé" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Princess Daisy", "Rosalina" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Louis Pasteur", "Joseph Lister" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "Thailand", "China" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Avengers: Endgame", "Titanic" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Commando", "Predator" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "White Rhino", "Giraffe", "Hippopotamus" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "Giraffe", "Hippopotamus", "White Rhino" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Freddie Mercury", "Elvis Presley", "Prince" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Pb", "Ag" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Amazon", "Yangtze", "Mississippi" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Prince", "Elvis Presley", "Freddie Mercury" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "9", "11", "13" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Nintendo", "Microsoft" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "China", "Thailand", "Vietnam" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Snake", "Crocodile", "Turtle" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1991", "1993", "1987" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Arctic Ocean", "Indian Ocean", "Atlantic Ocean" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Brad Pitt", "Matt Damon", "Tom Cruise" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Brazil", "Croatia" }),
        new("Music", "Who sang 'Rolling in the Deep'?", "Adele", new[] { "Beyoncé", "Rihanna", "Taylor Swift" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "Stone", "End Stone" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Croatia", "Brazil" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Respiration", "Transpiration", "Fermentation" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Arctic Ocean", "Atlantic Ocean", "Indian Ocean" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Apple", "Banana", "Strawberry" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Crocodile", "Turtle", "Snake" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Titanic", "Avengers: Endgame" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Tomato", "Peas", "Cucumber" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Louis Pasteur", "Joseph Lister" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Princess Daisy", "Pauline", "Rosalina" }),
        new("Sports", "How many players are there in a rugby union team?", "15", new[] { "13", "11", "9" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Joseph Lister", "Marie Curie" }),
        new("History", "In which year did the Berlin Wall fall?", "1989", new[] { "1987", "1993", "1991" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Yangtze", "Amazon", "Mississippi" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Brad Pitt", "Matt Damon", "Tom Cruise" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Indian Ocean", "Arctic Ocean" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Mark Zuckerberg", "Larry Page", "Steve Jobs" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Brad Pitt", "Matt Damon" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Central Program Utility", "Control Processing Unit", "Computer Personal Unit" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "The Rolling Stones", "Pink Floyd", "Queen" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Spaghetti", "Penne", "Fusilli" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Computer Personal Unit", "Central Program Utility", "Control Processing Unit" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Pb", "Gd", "Ag" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Crocodile", "Turtle", "Snake" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Joseph Lister", "Marie Curie" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Steve Jobs", "Mark Zuckerberg" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Penne", "Fusilli", "Spaghetti" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Computer Personal Unit", "Central Program Utility" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Peas", "Tomato", "Cucumber" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Matt Damon", "Brad Pitt" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Louis Pasteur", "Marie Curie", "Joseph Lister" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Indian Ocean", "Arctic Ocean", "Atlantic Ocean" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Larry Page", "Steve Jobs", "Mark Zuckerberg" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Benjamin Franklin", "John Adams", "Thomas Jefferson" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Arctic Ocean", "Indian Ocean", "Atlantic Ocean" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Commando", "Predator" }),
        new("Food & Drink", "What is the main ingredient in guacamole?", "Avocado",
            new[] { "Tomato", "Cucumber", "Peas" }),
        new("History", "Who discovered penicillin?", "Alexander Fleming",
            new[] { "Marie Curie", "Joseph Lister", "Louis Pasteur" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Matt Damon", "Brad Pitt" }),
        new("Sports", "Which country won the FIFA World Cup in 2018?", "France",
            new[] { "Germany", "Croatia", "Brazil" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Snake", "Turtle", "Crocodile" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Rosalina", "Princess Daisy", "Pauline" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Prince", "Elvis Presley", "Freddie Mercury" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Oxygen", "Nitrogen", "Helium" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Tom Cruise", "Brad Pitt", "Matt Damon" }),
        new("Science", "What gas do plants absorb from the atmosphere?", "Carbon Dioxide",
            new[] { "Helium", "Nitrogen", "Oxygen" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Amazon", "Mississippi", "Yangtze" }),
        new("Gaming", "Which company created the PlayStation?", "Sony", new[] { "Sega", "Nintendo", "Microsoft" }),
        new("Nature", "What is the largest land animal?", "African Elephant",
            new[] { "White Rhino", "Hippopotamus", "Giraffe" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Respiration", "Transpiration", "Fermentation" }),
        new("Entertainment", "Who played Jack in Titanic?", "Leonardo DiCaprio",
            new[] { "Brad Pitt", "Matt Damon", "Tom Cruise" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Pauline", "Princess Daisy", "Rosalina" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Central Program Utility", "Computer Personal Unit" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Indian Ocean", "Arctic Ocean", "Atlantic Ocean" }),
        new("Nature", "What kind of animal is a Komodo dragon?", "Lizard", new[] { "Crocodile", "Snake", "Turtle" }),
        new("Food & Drink", "What type of pasta's name means 'little worms' in Italian?", "Vermicelli",
            new[] { "Penne", "Spaghetti", "Fusilli" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Freddie Mercury", "Prince" }),
        new("Sports", "In tennis, what piece of fruit is found on top of the men's Wimbledon trophy?", "Pineapple",
            new[] { "Banana", "Strawberry", "Apple" }),
        new("Technology", "Who is the founder of Microsoft?", "Bill Gates",
            new[] { "Steve Jobs", "Larry Page", "Mark Zuckerberg" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "Stone", "End Stone" }),
        new("Technology", "In computing, what does 'CPU' stand for?", "Central Processing Unit",
            new[] { "Control Processing Unit", "Central Program Utility", "Computer Personal Unit" }),
        new("Gaming", "What is the name of the princess in the Mario series?", "Princess Peach",
            new[] { "Rosalina", "Pauline", "Princess Daisy" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Respiration", "Fermentation", "Transpiration" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Freddie Mercury", "Prince" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Ag", "Gd", "Pb" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Star Wars: The Force Awakens", "Avengers: Endgame", "Titanic" }),
        new("Food & Drink", "Which country is famous for sushi?", "Japan", new[] { "Vietnam", "Thailand", "China" }),
        new("Geography", "Which river runs through Egypt?", "Nile", new[] { "Yangtze", "Mississippi", "Amazon" }),
        new("Geography", "What is the capital of Australia?", "Canberra", new[] { "Brisbane", "Sydney", "Melbourne" }),
        new("Science", "What planet is known as the Red Planet?", "Mars", new[] { "Venus", "Jupiter", "Mercury" }),
        new("Music", "Which band released the album 'Abbey Road'?", "The Beatles",
            new[] { "Queen", "Pink Floyd", "The Rolling Stones" }),
        new("History", "Who was the first President of the United States?", "George Washington",
            new[] { "Benjamin Franklin", "John Adams", "Thomas Jefferson" }),
        new("Entertainment", "Which movie features the quote 'I'll be back'?", "The Terminator",
            new[] { "Total Recall", "Predator", "Commando" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Pb", "Gd", "Ag" }),
        new("Nature", "What is the process by which plants make their food?", "Photosynthesis",
            new[] { "Fermentation", "Respiration", "Transpiration" }),
        new("Music", "Who is known as the 'King of Pop'?", "Michael Jackson",
            new[] { "Elvis Presley", "Prince", "Freddie Mercury" }),
        new("Entertainment", "What is the highest-grossing film of all time (without inflation)?", "Avatar",
            new[] { "Titanic", "Avengers: Endgame", "Star Wars: The Force Awakens" }),
        new("Gaming", "In Minecraft, what do you need to make a Nether portal?", "Obsidian",
            new[] { "Netherrack", "End Stone", "Stone" }),
        new("Science", "What is the chemical symbol for gold?", "Au", new[] { "Gd", "Pb", "Ag" }),
        new("Geography", "What is the largest ocean on Earth?", "Pacific Ocean",
            new[] { "Atlantic Ocean", "Indian Ocean", "Arctic Ocean" }),
    };
}