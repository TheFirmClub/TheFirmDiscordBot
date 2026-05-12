using Discord;
using Discord.WebSocket;
using MySqlConnector;
using System;
using System.Linq;
using System.Threading.Tasks;

public class SeasonReactionRole
{
    // === CONFIG ===
    private const ulong ChannelId = 1501232157060890784;
    private const ulong VeteranRoleId = 1503220242430558288;

    private const string RequiredEmoji = "🥇";
    private const int RequiredMinutes = 150 * 60;

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
        try
        {
            var channel = _client.GetChannel(ChannelId) as IMessageChannel;

            if (channel == null)
            {
                Console.WriteLine("[SeasonReactionRole] Channel not found.");
                return;
            }

            // Skip if already posted
            var messages = await channel.GetMessagesAsync(10).FlattenAsync();

            bool alreadyExists = messages.Any(m =>
                m.Author.Id == _client.CurrentUser.Id &&
                m.Embeds.Any(e =>
                    e.Title == "🥇 Season 1 Veteran Claim"));

            if (alreadyExists)
            {
                Console.WriteLine("[SeasonReactionRole] Embed already exists.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🥇 Season 1 Veteran Claim")
                .WithDescription(
                    "React with 🥇 to verify your playtime.\n\n" +
                    "Requirement: **150+ hours** on The Firm.\n\n" +
                    $"Eligible players will automatically receive the <@&{VeteranRoleId}> role."
                )
                .WithColor(Color.Gold)
                .WithFooter("The Firm")
                .WithCurrentTimestamp()
                .Build();

            var message = await channel.SendMessageAsync(embed: embed);

            await message.AddReactionAsync(new Emoji(RequiredEmoji));

            Console.WriteLine($"[SeasonReactionRole] Embed posted.");
            Console.WriteLine($"[SeasonReactionRole] Message ID: {message.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SeasonReactionRole] Ready Error: {ex}");
        }
    }

    private async Task OnReactionAddedAsync(
        Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        SocketReaction reaction)
    {
        try
        {
            if (reaction.UserId == _client.CurrentUser.Id)
                return;

            if (reaction.Channel.Id != ChannelId)
                return;

            if (reaction.Emote.Name != RequiredEmoji)
                return;

            var reactedMessage = await cachedMessage.GetOrDownloadAsync();

            if (reactedMessage == null)
                return;

            if (!reactedMessage.Embeds.Any(e =>
                    e.Title != null &&
                    e.Title.Contains("Season 1 Veteran Claim")))
            {
                return;
            }

            var guildChannel = reaction.Channel as SocketGuildChannel;

            if (guildChannel == null)
                return;

            var guild = guildChannel.Guild;

            var user = guild.GetUser(reaction.UserId);

            if (user == null)
                return;

            string discordId = reaction.UserId.ToString();

            Console.WriteLine($"[SeasonReactionRole] Checking {user.Username}");

            var data = await FetchAsync(discordId);

            var message = await cachedMessage.GetOrDownloadAsync();

            // Failed requirement
            if (data == null || data.PlayTimeMinutes < RequiredMinutes)
            {
                await message.RemoveReactionAsync(new Emoji(RequiredEmoji), user);

                Console.WriteLine($"[SeasonReactionRole] Removed reaction from {user.Username}");

                try
                {
                    double hours = data?.PlayTimeMinutes / 60.0 ?? 0;

                    var failEmbed = new EmbedBuilder()
                        .WithTitle("Season 1 Veteran")
                        .WithDescription(
                            $"You do not currently meet the requirement.\n\n" +
                            $"Required: **150 hours**\n" +
                            $"Current: **{hours:0.#} hours**"
                        )
                        .WithColor(Color.Red)
                        .WithCurrentTimestamp()
                        .Build();

                    await user.SendMessageAsync(embed: failEmbed);
                }
                catch
                {
                    // DMs closed
                }

                return;
            }

            // Already has role
            if (user.Roles.Any(r => r.Id == VeteranRoleId))
            {
                Console.WriteLine($"[SeasonReactionRole] User already has role.");
                return;
            }

            // Give role
            await user.AddRoleAsync(VeteranRoleId);

            Console.WriteLine($"[SeasonReactionRole] Granted role to {user.Username}");

            // DM user
            try
            {
                var successEmbed = new EmbedBuilder()
                    .WithTitle("🥇 Congratulations!")
                    .WithDescription(
                        "You are now **The Firm's Season 1 Veteran**.\n\n" +
                        "Upon Season 2 launch, you may claim your perks at the **TownHall Clerk**."
                    )
                    .WithColor(Color.Gold)
                    .WithCurrentTimestamp()
                    .Build();

                await user.SendMessageAsync(embed: successEmbed);
            }
            catch
            {
                // DMs closed
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