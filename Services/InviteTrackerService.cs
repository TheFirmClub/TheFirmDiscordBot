using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class InviteTrackerService
{
    private readonly DiscordSocketClient _client;

    // Cache: GuildId -> InviteCode -> Uses
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>> _inviteCache = new();

    // Totals: GuildId -> InviterId -> count
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, int>> _totalsByInviter = new();

    // Per-code: GuildId -> InviterId -> (InviteCode -> count)
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>>> _byCode = new();

    private const string StatsFile = "invite_stats.json";

    public InviteTrackerService(DiscordSocketClient client)
    {
        _client = client;
        _client.Ready += OnReady;
        _client.UserJoined += OnUserJoined;

        // Your build expects these signatures:
        _client.InviteCreated += OnInviteCreated;                 // Task OnInviteCreated(SocketInvite)
        _client.InviteDeleted += OnInviteDeleted;                 // Task OnInviteDeleted(SocketGuildChannel, string)
    }

    public async Task InitializeAsync()
    {
        LoadStatsFromDisk();

        if (_client.LoginState == LoginState.LoggedIn &&
            _client.ConnectionState == ConnectionState.Connected)
        {
            await WarmInviteCacheForAllGuilds();
        }
    }

    private async Task OnReady()
    {
        await WarmInviteCacheForAllGuilds();
    }

    private async Task WarmInviteCacheForAllGuilds()
    {
        var tasks = _client.Guilds.Select(WarmInviteCacheForGuild);
        await Task.WhenAll(tasks);
    }

    private async Task WarmInviteCacheForGuild(SocketGuild guild)
    {
        try
        {
            var invites = await guild.GetInvitesAsync(); // requires Manage Guild
            var map = new ConcurrentDictionary<string, int>(
                invites.Select(i => new KeyValuePair<string, int>(i.Code, N(i.Uses)))
            );
            _inviteCache[guild.Id] = map;

            _totalsByInviter.TryAdd(guild.Id, new ConcurrentDictionary<ulong, int>());
            _byCode.TryAdd(guild.Id, new ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>>());
        }
        catch
        {
            // No permission or temporary error -> skip for this guild
        }
    }

    // ---- Event handlers (signatures for your version) ----

    private Task OnInviteCreated(SocketInvite invite)
    {
        ulong guildId = U(invite.Guild?.Id); // normalize ulong?
        if (guildId == 0UL) return Task.CompletedTask;

        var map = _inviteCache.GetOrAdd(guildId, _ => new ConcurrentDictionary<string, int>());
        map[invite.Code] = N(invite.Uses); // normalize int?
        return Task.CompletedTask;
    }

    private Task OnInviteDeleted(SocketGuildChannel channel, string code)
    {
        ulong guildId = channel.Guild.Id; // non-nullable
        if (_inviteCache.TryGetValue(guildId, out var map))
            map.TryRemove(code, out _);
        return Task.CompletedTask;
    }

    private async Task OnUserJoined(SocketGuildUser user)
    {
        var guild = user.Guild;

        if (!_inviteCache.TryGetValue(guild.Id, out var beforeMap))
        {
            await WarmInviteCacheForGuild(guild); // init cache if missing
            _inviteCache.TryGetValue(guild.Id, out beforeMap);
        }

        IReadOnlyCollection<RestInviteMetadata>? afterInvites;
        try
        {
            afterInvites = await guild.GetInvitesAsync(); // requires Manage Guild
        }
        catch
        {
            return; // cannot inspect invites -> cannot attribute
        }

        var afterMap = afterInvites.ToDictionary(i => i.Code, i => N(i.Uses));

        RestInviteMetadata? usedInvite = null;

        // Find the invite whose Uses increased
        foreach (var i in afterInvites)
        {
            var beforeUses = (beforeMap != null && beforeMap.TryGetValue(i.Code, out var u)) ? u : 0;
            var afterUses = N(i.Uses);
            if (afterUses > beforeUses)
            {
                usedInvite = i;
                break;
            }
        }

        // Update cache to "after"
        _inviteCache[guild.Id] = new ConcurrentDictionary<string, int>(
            afterMap.Select(kv => new KeyValuePair<string, int>(kv.Key, kv.Value))
        );

        if (usedInvite?.Inviter?.Id is ulong inviterId)
        {
            // Ignore self-invite (user rejoining via their own link)
            if (inviterId == user.Id)
                return;

            var totals = _totalsByInviter.GetOrAdd(guild.Id, _ => new ConcurrentDictionary<ulong, int>());
            totals.AddOrUpdate(inviterId, 1, (_, old) => old + 1);

            var perCodeForGuild = _byCode.GetOrAdd(guild.Id, _ => new ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>>());
            var perCodeForInviter = perCodeForGuild.GetOrAdd(inviterId, _ => new ConcurrentDictionary<string, int>());
            perCodeForInviter.AddOrUpdate(usedInvite.Code, 1, (_, old) => old + 1);

            SaveStatsToDisk();
        }
        // vanity/unknown invites don't credit anyone
    }

    // -------- Public API --------

    public int GetUserTotal(ulong guildId, ulong inviterId)
    {
        if (_totalsByInviter.TryGetValue(guildId, out var g) && g.TryGetValue(inviterId, out var count))
            return count;
        return 0;
    }

    public List<(ulong UserId, int Count)> GetTopInviters(ulong guildId, int take)
    {
        if (_totalsByInviter.TryGetValue(guildId, out var g))
            return g.OrderByDescending(x => x.Value).Take(take).Select(x => (x.Key, x.Value)).ToList();
        return new();
    }

    public List<(string Code, int Count)> GetPerCodeBreakdown(ulong guildId, ulong inviterId)
    {
        if (_byCode.TryGetValue(guildId, out var g) && g.TryGetValue(inviterId, out var map))
            return map.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
        return new();
    }

    // ---------- NEW: Clear Stats ----------

    public void ClearGuildStats(ulong guildId)
    {
        _totalsByInviter.TryRemove(guildId, out _);
        _byCode.TryRemove(guildId, out _);
        SaveStatsToDisk();
    }

    // -------- Persistence --------

    private void SaveStatsToDisk()
    {
        try
        {
            var dto = new PersistDto
            {
                Totals = _totalsByInviter.ToDictionary(
                    g => g.Key.ToString(),
                    g => g.Value.ToDictionary(x => x.Key.ToString(), x => x.Value)
                ),
                PerCode = _byCode.ToDictionary(
                    g => g.Key.ToString(),
                    g => g.Value.ToDictionary(
                        inviter => inviter.Key.ToString(),
                        inviter => inviter.Value.ToDictionary(code => code.Key, code => code.Value)
                    )
                )
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StatsFile, json);
        }
        catch { /* ignore */ }
    }

    private void LoadStatsFromDisk()
    {
        try
        {
            if (!File.Exists(StatsFile)) return;

            var json = File.ReadAllText(StatsFile);
            var dto = JsonSerializer.Deserialize<PersistDto>(json);
            if (dto == null) return;

            foreach (var (gStr, map) in dto.Totals)
            {
                if (!ulong.TryParse(gStr, out var gid)) continue;
                var inner = new ConcurrentDictionary<ulong, int>(map.Select(kv =>
                    new KeyValuePair<ulong, int>(ulong.Parse(kv.Key), kv.Value)));
                _totalsByInviter[gid] = inner;
            }

            foreach (var (gStr, invMap) in dto.PerCode)
            {
                if (!ulong.TryParse(gStr, out var gid)) continue;
                var invDict = new ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>>();
                foreach (var (invStr, codes) in invMap)
                {
                    if (!ulong.TryParse(invStr, out var inviterId)) continue;
                    invDict[inviterId] = new ConcurrentDictionary<string, int>(codes);
                }
                _byCode[gid] = invDict;
            }
        }
        catch { /* ignore */ }
    }

    // -------- Helpers (make code compile across versions) --------

    private static int N(int? v) => v ?? 0;        // normalize nullable int to int
    private static ulong U(ulong? v) => v ?? 0UL;  // normalize nullable ulong to ulong

    private class PersistDto
    {
        public Dictionary<string, Dictionary<string, int>> Totals { get; set; } = new();
        public Dictionary<string, Dictionary<string, Dictionary<string, int>>> PerCode { get; set; } = new();
    }
}
