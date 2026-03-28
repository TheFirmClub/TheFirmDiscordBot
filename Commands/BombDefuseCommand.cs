using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

public class BombDefuseCommand : ISlashCommand
{
    public string Name => "bomb";
    public string Description => "Challenge someone to defuse a bomb.";

    private static readonly Dictionary<ulong, GameState> ActiveGames = new();

    private readonly ulong[] allowedRoles = new ulong[]
    {
        1393729574537396355,
        1393623589122736238,
        1393590761953558608
    };

    private bool HasPermission(SocketGuildUser user) =>
        user.Roles.Any(r => allowedRoles.Contains(r.Id));

    private enum Wire
    {
        Red,
        Blue,
        Green,
        Yellow
    }

    private record GameState(
        ulong MessageId,
        ulong ChannelId,
        ulong StarterId,
        ulong P1Id,
        ulong P2Id,
        Wire CorrectWire,
        string Hint,
        bool Finished = false
    );

    private static readonly string[] FakeHints = new[]
    {
        "🔎 The correct wire is NOT red.",
        "🔎 Blue is definitely safe... right?",
        "🔎 The correct wire is an even position.",
        "🔎 Green looks suspicious.",
        "🔎 Yellow might be a trap.",
        "🔎 Trust your instincts...",
        "🔎 One of these wires will end badly.",
        "🔎 The answer is simpler than you think."
    };

    public SlashCommandProperties Build()
    {
        return new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(Description)
            .AddOption("opponent", ApplicationCommandOptionType.User, "Who do you challenge?", true)
            .Build();
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("❌ Use this in a server.", ephemeral: true);
            return;
        }

        if (!HasPermission(user))
        {
            await command.RespondAsync("❌ No permission.", ephemeral: true);
            return;
        }

        var opp = (SocketGuildUser)command.Data.Options.First().Value!;
        if (opp.Id == user.Id)
        {
            await command.RespondAsync("❌ You can’t challenge yourself.", ephemeral: true);
            return;
        }

        if (ActiveGames.ContainsKey(command.ChannelId!.Value))
        {
            await command.RespondAsync("⚠️ A bomb game is already active here.", ephemeral: true);
            return;
        }

        var rand = new Random();
        var correctWire = (Wire)rand.Next(0, 4);
        var hint = FakeHints[rand.Next(FakeHints.Length)];

        var embed = new EmbedBuilder()
            .WithTitle("💣 Bomb Defuse")
            .WithDescription(
                $"**Players**\n• {user.Mention}\n• {opp.Mention}\n\n" +
                $"⏱️ You have **30 seconds**!\n\n" +
                $"💡 Hint:\n{hint}\n\n" +
                $"Cut the correct wire… or explode.")
            .WithColor(Color.Orange)
            .Build();

        await command.RespondAsync(embed: embed, components: BuildButtons("pending"));
        var msg = await command.GetOriginalResponseAsync();

        var state = new GameState(
            msg.Id,
            msg.Channel.Id,
            user.Id,
            user.Id,
            opp.Id,
            correctWire,
            hint,
            false
        );

        ActiveGames[state.ChannelId] = state;

        await msg.ModifyAsync(m => { m.Components = BuildButtons(state.MessageId.ToString()); });

        // ⏱️ AUTO EXPLODE TIMER
        _ = Task.Run(async () =>
        {
            await Task.Delay(30000);

            if (ActiveGames.TryGetValue(state.ChannelId, out var current) && current.MessageId == state.MessageId)
            {
                ActiveGames.Remove(state.ChannelId);

                var explodeEmbed = new EmbedBuilder()
                    .WithTitle("💣 Bomb Result")
                    .WithDescription("💥 Time’s up! The bomb exploded!")
                    .WithColor(Color.DarkRed)
                    .Build();

                try
                {
                    var channel = msg.Channel;
                    var originalMsg = await channel.GetMessageAsync(state.MessageId) as IUserMessage;

                    if (originalMsg != null)
                    {
                        await originalMsg.ModifyAsync(m =>
                        {
                            m.Embed = explodeEmbed;
                            m.Components = DisableButtons(state.MessageId.ToString());
                        });
                    }
                }
                catch
                {
                    /* message might be deleted */
                }
            }
        });
    }

    public static Task HandleButton(SocketMessageComponent component)
        => HandleComponentAsync(component);

    public static async Task HandleComponentAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId is null || !component.Data.CustomId.StartsWith("bomb:"))
            return;

        var parts = component.Data.CustomId.Split(':');
        if (parts.Length < 3) return;

        var action = parts[1];
        if (!ulong.TryParse(parts[2], out var messageId)) return;

        var channelId = component.Channel.Id;

        if (!ActiveGames.TryGetValue(channelId, out var state) || state.MessageId != messageId)
        {
            await component.RespondAsync("Game not active.", ephemeral: true);
            return;
        }

        // Cancel
        if (action == "cancel")
        {
            if (component.User.Id != state.StarterId)
            {
                await component.RespondAsync("Only starter can cancel.", ephemeral: true);
                return;
            }

            ActiveGames.Remove(channelId);

            await component.UpdateAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithTitle("💣 Bomb Defuse")
                    .WithDescription("🛑 Game cancelled.")
                    .WithColor(Color.DarkGrey)
                    .Build();

                m.Components = DisableButtons(messageId.ToString());
            });

            return;
        }

        // Wire pick
        if (action == "cut")
        {
            if (component.User.Id != state.P1Id && component.User.Id != state.P2Id)
            {
                await component.RespondAsync("You’re not in this game.", ephemeral: true);
                return;
            }

            if (parts.Length < 4) return;
            if (!Enum.TryParse<Wire>(parts[3], out var chosenWire)) return;

            var win = chosenWire == state.CorrectWire;

            ActiveGames.Remove(channelId);

            var embed = new EmbedBuilder()
                .WithTitle("💣 Bomb Result")
                .WithDescription(
                    $"**{component.User.Mention} cut the {chosenWire} wire!**\n\n" +
                    (win
                        ? "✅ Correct wire! Bomb defused!"
                        : $"💥 BOOM! Wrong wire! Correct was **{state.CorrectWire}**"))
                .WithColor(win ? Color.Green : Color.Red)
                .Build();

            await component.UpdateAsync(m =>
            {
                m.Embed = embed;
                m.Components = DisableButtons(messageId.ToString());
            });
        }
    }

    // ===== Buttons =====

    private static MessageComponent BuildButtons(string id)
    {
        return new ComponentBuilder()
            .WithButton("🔴 Red", $"bomb:cut:{id}:Red", ButtonStyle.Danger)
            .WithButton("🔵 Blue", $"bomb:cut:{id}:Blue", ButtonStyle.Primary)
            .WithButton("🟢 Green", $"bomb:cut:{id}:Green", ButtonStyle.Success)
            .WithButton("🟡 Yellow", $"bomb:cut:{id}:Yellow", ButtonStyle.Secondary)
            .WithButton("Cancel", $"bomb:cancel:{id}", ButtonStyle.Danger)
            .Build();
    }

    private static MessageComponent DisableButtons(string id)
    {
        return new ComponentBuilder()
            .WithButton("🔴 Red", $"bomb:off:{id}:Red", ButtonStyle.Secondary, disabled: true)
            .WithButton("🔵 Blue", $"bomb:off:{id}:Blue", ButtonStyle.Secondary, disabled: true)
            .WithButton("🟢 Green", $"bomb:off:{id}:Green", ButtonStyle.Secondary, disabled: true)
            .WithButton("🟡 Yellow", $"bomb:off:{id}:Yellow", ButtonStyle.Secondary, disabled: true)
            .WithButton("Cancel", $"bomb:off:{id}:Cancel", ButtonStyle.Secondary, disabled: true)
            .Build();
    }
}