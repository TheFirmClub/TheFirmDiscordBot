using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class ChangelogTrackerService
{
    // ===== Behavior =====
    private const int AutoPostDebounceSeconds = 60; // wait after last change to update

    // One-time history backfill on startup
    private const bool BackfillOnStartup = true;
    private const int BackfillMaxMessages = 10000; // cap to avoid rate limits; adjust as needed

    private readonly DiscordSocketClient _client;

    // Channel IDs
    private const ulong SourceChannelId = 1393640613576179752UL;
    private const ulong StatsChannelId  = 1393637573385126058UL;
    
    private static readonly HashSet<ulong> AllowedRoleIds = new()
    {
        1407655775077404806UL, // junior dev
        1393733280523882546UL, // developer
        1421177514189127840UL, // senior dev
        1393590761953558608UL  // senior management
    };

    // Data and persistence
    private readonly ConcurrentDictionary<ulong, UserStats> _byUser = new();
    private readonly ConcurrentDictionary<ulong, int> _messageItemCounts = new(); // messageId -> item count
    private readonly ConcurrentDictionary<ulong, ulong> _messageAuthors = new();   // messageId -> authorId

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private const string StatsPath = "changelog_stats.json";

    // Post/update controls
    private System.Timers.Timer? _debounceTimer;
    private ulong? _lastStatsMessageId; // delete this before posting a fresh embed (persisted)

    public ChangelogTrackerService(DiscordSocketClient client)
    {
        _client = client;
        _client.Ready += OnReady;
        _client.MessageReceived += OnMessageReceived;
        _client.MessageUpdated += OnMessageUpdated;
        _client.MessageDeleted += OnMessageDeleted;
    }

    private async Task OnReady()
    {
        Load();
        Console.WriteLine($"ChangelogTrackerService ready. Last posted stats message id: {_lastStatsMessageId?.ToString() ?? "<none>"}");

        if (BackfillOnStartup && _byUser.IsEmpty)
        {
            await BackfillHistoryAsync();
            Save();
            await PostStatsEmbed();
            Console.WriteLine("[ChangelogTracker] Backfill completed.");
        }
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        if (msg.Author.IsBot) return;
        if (msg.Channel.Id != SourceChannelId) return;

        int items = CountChangelogItems(msg.Content);
        if (items <= 0) return;

        var displayName = (msg.Author as SocketGuildUser)?.DisplayName ?? msg.Author.Username;
        Tally(msg.Author, items, displayName);
        _messageItemCounts[msg.Id] = items;
        _messageAuthors[msg.Id] = msg.Author.Id;
        Save();
        ScheduleDebouncedPost();
    }

    private async Task OnMessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        try
        {
            if (after.Author.IsBot) return;
            if (channel.Id != SourceChannelId) return;

            int newItems = CountChangelogItems(after.Content ?? string.Empty);
            if (!_messageItemCounts.TryGetValue(after.Id, out int oldItems))
            {
                if (newItems > 0)
                {
                    var displayName = (after.Author as SocketGuildUser)?.DisplayName ?? after.Author.Username;
                    Tally(after.Author, newItems, displayName);
                    _messageItemCounts[after.Id] = newItems;
                    _messageAuthors[after.Id] = after.Author.Id;
                    Save();
                    ScheduleDebouncedPost();
                }
                return;
            }

            int delta = newItems - oldItems;
            if (delta != 0 && _messageAuthors.TryGetValue(after.Id, out ulong authorId))
            {
                ApplyDelta(authorId, delta);
                _messageItemCounts[after.Id] = newItems;
                Save();
                ScheduleDebouncedPost();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChangelogTracker] Update handling error: {ex}");
        }
    }

    private Task OnMessageDeleted(Cacheable<IMessage, ulong> cache, Cacheable<IMessageChannel, ulong> channel)
    {
        try
        {
            if (channel.Id != SourceChannelId) return Task.CompletedTask;

            if (_messageItemCounts.TryRemove(cache.Id, out int items)
                && _messageAuthors.TryRemove(cache.Id, out ulong authorId))
            {
                ApplyDelta(authorId, -items);
                Save();
                ScheduleDebouncedPost();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChangelogTracker] Delete handling error: {ex}");
        }
        return Task.CompletedTask;
    }

    // ===== Backfill (history scan) =====
    private async Task BackfillHistoryAsync()
    {
        if (!(_client.GetChannel(SourceChannelId) is ITextChannel chan))
        {
            Console.WriteLine($"[ChangelogTracker] Source channel {SourceChannelId} not found or not a text channel.");
            return;
        }

        Console.WriteLine($"[ChangelogTracker] Starting backfill (up to {BackfillMaxMessages} messages)...");
        int fetched = 0;
        ulong? beforeMessageId = null;

        while (fetched < BackfillMaxMessages)
        {
            var take = Math.Min(100, BackfillMaxMessages - fetched);
            IEnumerable<IMessage> batch;

            if (beforeMessageId.HasValue)
                batch = await chan.GetMessagesAsync(beforeMessageId.Value, Direction.Before, take).FlattenAsync();
            else
                batch = await chan.GetMessagesAsync(take).FlattenAsync();

            var list = batch.ToList();
            if (list.Count == 0) break;

            foreach (var m in list)
            {
                if (m.Author.IsBot) continue;

                int items = CountChangelogItems(m.Content);
                if (items <= 0) continue;

                // Prefer guild display name
                string displayName = m.Author.Username;
                try
                {
                    var gu = await chan.GetUserAsync(m.Author.Id);
                    if (gu != null) displayName = gu.DisplayName ?? gu.Username;
                }
                catch { }

                // Tally
                Tally(m.Author, items, displayName);

                // Track counts for future edit/delete consistency
                _messageItemCounts[m.Id] = items;
                _messageAuthors[m.Id] = m.Author.Id;
            }

            fetched += list.Count;
            beforeMessageId = list.Last().Id;

            // Be polite to rate limits
            await Task.Delay(400);
        }

        Console.WriteLine($"[ChangelogTracker] Backfill scanned {fetched} messages.");
    }

    // ===== Posting =====
    private async Task PostStatsEmbed()
    {
        var statsChannel = _client.GetChannel(StatsChannelId) as IMessageChannel;
        if (statsChannel == null)
        {
            Console.WriteLine($"Stats channel {StatsChannelId} not found or not a text channel.");
            return;
        }

        var guild = (_client.GetChannel(SourceChannelId) as SocketGuildChannel)?.Guild;

        var filteredUsers = _byUser.Values.Where(u =>
        {
            if (guild == null) return false;

            var member = guild.GetUser(u.UserId);
            if (member == null) return false;

            return member.Roles.Any(r => AllowedRoleIds.Contains(r.Id));
        });

    // optional: clean total (only current team)
        int totalItems = filteredUsers.Sum(s => s.Items);

        var top10 = filteredUsers
            .OrderByDescending(s => s.Items)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        var desc = new StringBuilder();
        int rank = 1;
        foreach (var u in top10)
        {
            desc.AppendLine($"**{rank}.** {Escape(u.DisplayName)} — **{u.Items}** items");
            rank++;
        }

        var embed = new EmbedBuilder()
            .WithTitle("📦 Changelog Contribution Stats")
            .WithDescription(desc.Length > 0 ? desc.ToString() : "No data yet. Post some bullets in the changelog channel!")
            .WithColor(new Color(155, 100, 255))
            .WithThumbnailUrl("https://www.thefirm.club/Media/thefirm-thumb.png")
            .AddField("Total Items", $"**{totalItems}**", true)
            .WithFooter($"Updated • {DateTimeOffset.Now:yyyy-MM-dd HH:mm}")
            .Build();

        try
        {
            if (_lastStatsMessageId.HasValue)
            {
                var oldMsg = await statsChannel.GetMessageAsync(_lastStatsMessageId.Value);
                if (oldMsg != null)
                {
                    await oldMsg.DeleteAsync();
                }
            }

            var newMsg = await statsChannel.SendMessageAsync(embed: embed);
            _lastStatsMessageId = newMsg.Id;
            Save(); // persist the new message id so we can delete after restarts
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChangelogTracker] Failed to post stats embed: {ex.Message}");
        }
    }

    // ===== Helpers =====
    private static string Escape(string s)
    {
        return s.Replace("*", "\\*").Replace("_", "\\_").Replace("~", "\\~").Replace("`", "\\`");
    }

    private void Tally(IUser author, int items, string? displayNameOverride = null)
    {
        var stats = _byUser.GetOrAdd(author.Id, _ => new UserStats
        {
            UserId = author.Id,
            DisplayName = displayNameOverride ?? author.Username
        });

        // Keep display name fresh
        stats.DisplayName = displayNameOverride ?? author.Username;
        stats.Items += items;
        stats.LastAt = DateTimeOffset.UtcNow;

        if (stats.FirstAt == default)
            stats.FirstAt = stats.LastAt;
    }

    private void ApplyDelta(ulong userId, int deltaItems)
    {
        var stats = _byUser.GetOrAdd(userId, _ => new UserStats { UserId = userId, DisplayName = userId.ToString() });
        stats.Items = Math.Max(0, stats.Items + deltaItems);
        stats.LastAt = DateTimeOffset.UtcNow;
    }

    private static int CountChangelogItems(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;

        int count = 0;
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") || trimmed == "-" || trimmed.StartsWith("• ") || trimmed.StartsWith("* "))
            {
                count++;
            }
        }
        return count;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StatsPath)) return;
            var json = File.ReadAllText(StatsPath);

            // Try new persisted-state format first
            PersistedState? state = null;
            try { state = JsonSerializer.Deserialize<PersistedState>(json, _jsonOptions); }
            catch { /* fall back */ }

            if (state != null && state.Users != null && state.Users.Count > 0)
            {
                _byUser.Clear();
                foreach (var kv in state.Users)
                    _byUser[kv.Key] = kv.Value;
                _lastStatsMessageId = state.LastMessageId;
                return;
            }

            // Legacy format fallback: plain dictionary<userId, UserStats>
            var legacy = JsonSerializer.Deserialize<Dictionary<ulong, UserStats>>(json, _jsonOptions);
            if (legacy != null)
            {
                _byUser.Clear();
                foreach (var kv in legacy)
                    _byUser[kv.Key] = kv.Value;
                _lastStatsMessageId = null; // not stored in legacy file
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChangelogTracker] Failed to load stats: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var state = new PersistedState
            {
                LastMessageId = _lastStatsMessageId,
                Users = _byUser.ToDictionary(k => k.Key, v => v.Value)
            };
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            File.WriteAllText(StatsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChangelogTracker] Failed to save stats: {ex.Message}");
        }
    }

    private void ScheduleDebouncedPost()
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new System.Timers.Timer(AutoPostDebounceSeconds * 1000)
            {
                AutoReset = false
            };
            _debounceTimer.Elapsed += async (_, __) =>
            {
                try { await PostStatsEmbed(); }
                catch (Exception ex) { Console.WriteLine($"[ChangelogTracker] Debounced post failed: {ex}"); }
            };
        }
        else
        {
            _debounceTimer.Stop();
        }
        _debounceTimer.Interval = AutoPostDebounceSeconds * 1000;
        _debounceTimer.Start();
    }

    // ===== Models =====
    public class UserStats
    {
        public ulong UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int Items { get; set; }
        public DateTimeOffset FirstAt { get; set; }
        public DateTimeOffset LastAt { get; set; }
    }

    public class PersistedState
    {
        [JsonPropertyName("lastMessageId")] public ulong? LastMessageId { get; set; }
        [JsonPropertyName("users")] public Dictionary<ulong, UserStats> Users { get; set; } = new();
    }
}