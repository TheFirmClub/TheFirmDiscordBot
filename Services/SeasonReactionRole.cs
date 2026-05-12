using Discord;
using Discord.WebSocket;
using MySqlConnector;
using System;
using System.Linq;
using System.Threading.Tasks;

public class SeasonReactionRole
{
    private const ulong ChannelId = 1501232157060890784;
    private const ulong VeteranRoleId = 1503220242430558288;

    private const string RequiredEmoji = "🥇";
    private const int RequiredMinutes = 150 * 60;

    private const string EmbedTitle = "🥇 Season #1 Veteran Claim";

    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    private readonly DiscordSocketClient _client;

    public SeasonReactionRole(DiscordSocketClient client)
    {
        _client = client;

        _client.Ready += () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await OnBotReady();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ SeasonReactionRole Ready Error: {ex}");
                }
            });

            return Task.CompletedTask;
        };

        _client.ReactionAdded += OnReactionAddedAsync;
    }

    private async Task OnBotReady()
    {
        var channel = _client.GetChannel(ChannelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("[SeasonReactionRole] Channel not found.");
            return;
        }

        var messages = await channel.GetMessagesAsync(10).FlattenAsync();

        bool alreadyExists = messages.Any(m =>
            m.Author.Id == _client.CurrentUser.Id &&
            m.Embeds.Any(e => e.Title == EmbedTitle));

        if (alreadyExists)
        {
            Console.WriteLine("[SeasonReactionRole] Embed already exists. Skipping.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(EmbedTitle)
            .WithDescription(
                "React with 🥇 to verify your playtime.\n\n" +
                "Requirement: **150+ hours** on The Firm.\n\n" +
                $"Eligible players will automatically receive the <@&{VeteranRoleId}> role."
            )
            .WithColor(Color.Gold)
            .WithFooter("The Firm")
            .WithCurrentTimestamp()
            .Build();

        var message = await channel.SendMessageAsync(
            embed: embed,
            allowedMentions: AllowedMentions.All
        );

        await message.AddReactionAsync(new Emoji(RequiredEmoji));

        Console.WriteLine("[SeasonReactionRole] Embed posted.");
    }

    private async Task OnReactionAddedAsync(
        Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        SocketReaction reaction)
    {
        try
        {
            // Ignore bot reactions
            if (reaction.UserId == _client.CurrentUser.Id)
                return;

            // Wrong channel
            if (reaction.Channel.Id != ChannelId)
                return;

            // Wrong emoji
            if (reaction.Emote.Name != RequiredEmoji)
                return;

            var reactedMessage = await cachedMessage.GetOrDownloadAsync();

            if (reactedMessage == null)
                return;

            // Make sure reaction is on correct embed
            bool isCorrectEmbed = reactedMessage.Embeds.Any(e =>
                e.Title != null &&
                e.Title == EmbedTitle);

            if (!isCorrectEmbed)
                return;

            var guildChannel = reaction.Channel as SocketGuildChannel;

            if (guildChannel == null)
                return;

            var guild = guildChannel.Guild;
            var user = guild.GetUser(reaction.UserId);

            if (user == null)
                return;

            // Already has role → remove reaction immediately
            if (user.Roles.Any(r => r.Id == VeteranRoleId))
            {
                await reactedMessage.RemoveReactionAsync(
                    new Emoji(RequiredEmoji),
                    user
                );

                Console.WriteLine($"[SeasonReactionRole] {user.Username} already has role.");

                return;
            }

            string discordId = reaction.UserId.ToString();

            Console.WriteLine($"[SeasonReactionRole] Checking {user.Username} ({discordId})");

            var data = await FetchAsync(discordId);

            // Failed requirement
            if (data == null || data.PlayTimeMinutes < RequiredMinutes)
            {
                await reactedMessage.RemoveReactionAsync(
                    new Emoji(RequiredEmoji),
                    user
                );

                Console.WriteLine($"[SeasonReactionRole] Removed reaction from {user.Username}");

                return;
            }

            // Give role
            await user.AddRoleAsync(VeteranRoleId);

            Console.WriteLine($"[SeasonReactionRole] Granted role to {user.Username}");

            // DM user
            try
            {
                var dmEmbed = new EmbedBuilder()
                    .WithTitle("🥇 Congratulations!")
                    .WithDescription(
                        "You are now **The Firm's Season #1 Veteran**.\n\n" +
                        "Upon Season 2 launch, you may claim your perks at the **TownHall Clerk**."
                    )
                    .WithColor(Color.Gold)
                    .WithFooter("The Firm")
                    .WithCurrentTimestamp()
                    .Build();

                await user.SendMessageAsync(embed: dmEmbed);
            }
            catch
            {
                Console.WriteLine($"[SeasonReactionRole] Could not DM {user.Username}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SeasonReactionRole ERROR] {ex}");
        }
    }

    private async Task<PlayerData?> FetchAsync(string discordId)
    {
        const string sql = @"
            SELECT 
                license,
                display_name,
                discord_id,
                play_time,
                ts_last_connection,
                ts_joined,
                updated_at
            FROM player_playtime
            WHERE discord_id = @id
            ORDER BY ts_last_connection DESC
            LIMIT 1;
        ";

        await using var conn = new MySqlConnection(ConnectionString);

        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@id", discordId);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new PlayerData
        {
            License = reader["license"]?.ToString() ?? "",
            DisplayName = reader["display_name"]?.ToString() ?? "",
            DiscordId = reader["discord_id"]?.ToString() ?? discordId,
            PlayTimeMinutes = Convert.ToInt32(reader["play_time"]),
            TsLastConnection = Convert.ToInt64(reader["ts_last_connection"]),
            TsJoined = Convert.ToInt64(reader["ts_joined"]),
            UpdatedAt = Convert.ToDateTime(reader["updated_at"])
        };
    }

    private class PlayerData
    {
        public string License { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DiscordId { get; set; } = "";
        public int PlayTimeMinutes { get; set; }
        public long TsLastConnection { get; set; }
        public long TsJoined { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}