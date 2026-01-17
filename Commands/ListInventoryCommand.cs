using Discord;
using Discord.WebSocket;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class ListInventoryCommand : ISlashCommand
{
    public string Name => "listinv";
    public string Description => "List all players who have a specific inventory item";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    // 🔒 Allowed roles
    private static readonly HashSet<ulong> AllowedRoleIds = new()
    {
        1393590761953558608, // SM
    };

    private const int PageSize = 10;

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser caller)
        {
            await command.RespondAsync("❌ Server-only command.", ephemeral: true);
            return;
        }

        // Role check
        if (!caller.Roles.Any(r => AllowedRoleIds.Contains(r.Id)))
        {
            await command.RespondAsync("❌ You do not have permission to use this command.", ephemeral: true);
            return;
        }

        if (command.Data.Options == null || command.Data.Options.Count == 0)
        {
            await command.RespondAsync("❌ Usage: `/listinv item:<item_name>`", ephemeral: true);
            return;
        }

        string itemName = command.Data.Options.First().Value!.ToString()!.Trim();

        await command.DeferAsync(ephemeral: false);

        var results = await LoadInventoryMatches(itemName);

        if (!results.Any())
        {
            await command.FollowupAsync($"ℹ️ No players found with `{itemName}`.");
            return;
        }

        var embeds = BuildEmbeds(results, itemName);

        var components = new ComponentBuilder()
            .WithButton("⬅️ Prev", "listinv_prev", disabled: true)
            .WithButton("Next ➡️", "listinv_next", disabled: embeds.Count <= 1);

        var message = await command.FollowupAsync(
            embed: embeds[0],
            components: components.Build()
        );

        PaginationStore.Add(message.Id, new PaginationState
        {
            Embeds = embeds,
            CurrentPage = 0,
            OriginalUserId = caller.Id
        });
    }

    // ---------- Database ----------

    private static async Task<List<ItemResult>> LoadInventoryMatches(string itemName)
    {
        var list = new List<ItemResult>();

        const string sql = @"
            SELECT citizenid, name, inventory
            FROM players
            WHERE inventory IS NOT NULL;
        ";

        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new MySqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        int cidCol = reader.GetOrdinal("citizenid");
        int nameCol = reader.GetOrdinal("name");
        int invCol = reader.GetOrdinal("inventory");

        while (await reader.ReadAsync())
        {
            string inventoryJson = reader.GetString(invCol);

            List<InventoryItem>? inventory;
            try
            {
                inventory = JsonSerializer.Deserialize<List<InventoryItem>>(inventoryJson);
            }
            catch
            {
                continue;
            }

            if (inventory == null) continue;

            foreach (var item in inventory)
            {
                if (item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new ItemResult
                    {
                        CharacterName = reader.GetString(nameCol),
                        CitizenId = reader.GetString(cidCol),
                        Count = item.Count > 0 ? item.Count : 1
                    });
                    break;
                }
            }
        }

        return list;
    }

    // ---------- Embeds ----------

    private static List<Embed> BuildEmbeds(List<ItemResult> results, string itemName)
    {
        var embeds = new List<Embed>();

        for (int i = 0; i < results.Count; i += PageSize)
        {
            var chunk = results.Skip(i).Take(PageSize);

            var embed = new EmbedBuilder()
                .WithTitle($"📦 Inventory Lookup — `{itemName}`")
                .WithColor(new Color(0x22, 0xC5, 0x5E))
                .WithFooter($"Results {i + 1}-{Math.Min(i + PageSize, results.Count)} of {results.Count}")
                .WithCurrentTimestamp();

            foreach (var r in chunk)
            {
                embed.AddField(
                    r.CharacterName,
                    $"🆔 `{r.CitizenId}`\n📊 **Count:** {r.Count}",
                    false
                );
            }

            embeds.Add(embed.Build());
        }

        return embeds;
    }

    // ---------- Models ----------

    private class InventoryItem
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    private class ItemResult
    {
        public string CharacterName { get; set; } = "";
        public string CitizenId { get; set; } = "";
        public int Count { get; set; }
    }

    // ---------- Pagination ----------

    private class PaginationState
    {
        public List<Embed> Embeds { get; set; } = new();
        public int CurrentPage { get; set; }
        public ulong OriginalUserId { get; set; }
    }

    private static class PaginationStore
    {
        private static readonly Dictionary<ulong, PaginationState> Pages = new();

        public static void Add(ulong messageId, PaginationState state) => Pages[messageId] = state;
        public static bool TryGet(ulong messageId, out PaginationState state) => Pages.TryGetValue(messageId, out state);
    }

    // ---------- Button Handler ----------

    public static async Task HandleButton(SocketMessageComponent component)
    {
        if (!PaginationStore.TryGet(component.Message.Id, out var state))
            return;

        if (component.User.Id != state.OriginalUserId)
        {
            await component.RespondAsync("❌ Only the command user can use these buttons.", ephemeral: true);
            return;
        }

        if (component.Data.CustomId == "listinv_next" && state.CurrentPage < state.Embeds.Count - 1)
            state.CurrentPage++;
        else if (component.Data.CustomId == "listinv_prev" && state.CurrentPage > 0)
            state.CurrentPage--;

        var buttons = new ComponentBuilder()
            .WithButton("⬅️ Prev", "listinv_prev", disabled: state.CurrentPage == 0)
            .WithButton("Next ➡️", "listinv_next", disabled: state.CurrentPage == state.Embeds.Count - 1);

        await component.UpdateAsync(msg =>
        {
            msg.Embed = state.Embeds[state.CurrentPage];
            msg.Components = buttons.Build();
        });
    }
}
