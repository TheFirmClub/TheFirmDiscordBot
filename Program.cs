using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    private DiscordSocketClient? _client;
    private SlashCommandHandler? _commandHandler;
    private RoleLogger? _roleLogger;
    private IConfiguration? _config;
    private SupportMenuHandler _supportMenuHandler = new();
    private TicketButtonHandler _ticketButtonHandler;
    private InviteTrackerService? _inviteTracker;

    private ulong _logChannelId = 1394449608603603085;

    // 🔹 FiveM integration
    private FiveMChannelUpdater? _fivemUpdater;

    public static Task Main(string[] args) => new Program().MainAsync();

    public async Task MainAsync()
    {
        _config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var guildIdString = _config["Discord:GuildId"];
        if (!ulong.TryParse(guildIdString, out ulong guildId))
        {
            Console.WriteLine("❌ Invalid or missing GuildId in appsettings.json");
            return;
        }

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds |
                             GatewayIntents.GuildMessages |
                             GatewayIntents.MessageContent |
                             GatewayIntents.GuildMembers |
                             GatewayIntents.GuildPresences |
                             GatewayIntents.GuildMessageReactions,
            LogLevel = LogSeverity.Info
        });

        _client.Log += Log;
        _client.Ready += async () => await ReadyAsync(guildId);

        new StoreEmbed(_client);
        
        // after _client is created
        _inviteTracker = new InviteTrackerService(_client);
        await _inviteTracker.InitializeAsync();

        _commandHandler = new SlashCommandHandler(_config, _inviteTracker);
        
        _ticketButtonHandler = new TicketButtonHandler(_config);
        
        _client.SlashCommandExecuted += SlashCommandExecuted;
        _client.SelectMenuExecuted += _supportMenuHandler.HandleAsync;
        _client.ModalSubmitted += new SupportModalHandler().HandleModalAsync;
        _client.ButtonExecuted += async component =>
        {
            await _ticketButtonHandler.HandleAsync(component);
            await TicTacToeCommand.HandleButton(component);
            await RpsCommand.HandleComponentAsync(component);
            await TriviaCommand.HandleButton(component);
        };

        ulong roleLogChannelId = 1393726185804005497;
        _roleLogger = new RoleLogger(_client, roleLogChannelId);

        string? token = _config["Discord:Token"];
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("❌ Discord token missing in appsettings.json");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private async Task ReadyAsync(ulong guildId)
    {
        var guild = _client!.GetGuild(guildId);
        if (guild == null)
        {
            Console.WriteLine($"❌ Could not find guild with ID {guildId}");
            return;
        }

        foreach (var command in _commandHandler!.GetAllCommands())
        {
            var builder = new SlashCommandBuilder()
                .WithName(command.Name)
                .WithDescription(command.Description);

            if (command.Name == "ticketadduser")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to add to the ticket", true);
            else if (command.Name == "ticketaddrole")
                builder.AddOption("role", ApplicationCommandOptionType.Role, "Role to add to the ticket", true);
            else if (command.Name == "ticketrestrict")
                builder.AddOption("role", ApplicationCommandOptionType.Role, "Role to restrict this ticket to", true);
            else if (command.Name == "tempticket")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to open the temporary ticket for", true);
            else if (command.Name == "addrole" || command.Name == "removerole")
            {
                builder.AddOption("user", ApplicationCommandOptionType.User, "Target user", true);
                builder.AddOption("role", ApplicationCommandOptionType.Role, "Role to assign/remove", true);
            }
            else if (command.Name == "checkrole")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to check", true);
            else if (command.Name == "8ball")
                builder.AddOption("question", ApplicationCommandOptionType.String, "Your question for the magic 8-ball", true);
            else if (command.Name == "tictactoe")
                builder.AddOption("opponent", ApplicationCommandOptionType.User, "User to challenge", true);
            else if (command.Name == "slap")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to slap", true);
            else if (command.Name == "rps")
                builder.AddOption("opponent", ApplicationCommandOptionType.User, "User to challenge", true);
            else if (command.Name == "trivia")
                builder.AddOption("category", ApplicationCommandOptionType.String, "Limit to a category (General, Gaming, Science, History, Tech, Movies)", false);
            else if (command.Name == "rickroll")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to (totally not) rickroll", false);
            else if (command.Name == "fakeban")
            {
                builder.AddOption("user", ApplicationCommandOptionType.User, "Who to fake ban", true);
                builder.AddOption("reason", ApplicationCommandOptionType.String, "Reason for the fake ban", false);
            }

            else if (command.Name == "screamer")
            {
                // no options
            }
            else if (command.Name == "reverse")
                builder.AddOption("text", ApplicationCommandOptionType.String, "Text to reverse", true);
            else if (command.Name == "tinytext")
                builder.AddOption("text", ApplicationCommandOptionType.String, "Text to shrink", true);
            else if (command.Name == "lag")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to pretend is lagging", true);
            else if (command.Name == "sus")
                builder.AddOption("user", ApplicationCommandOptionType.User, "User who is kinda sus", false);
            else if (command.Name == "hack")
                builder.AddOption("user", ApplicationCommandOptionType.User, "Target to (pretend) hack", false);
            else if (command.Name == "cursedimage")
            {
                // no options
            }
            else if (command.Name == "loud")
            {
                builder.AddOption("user", ApplicationCommandOptionType.User, "Who to appear to ping", true);
                builder.AddOption(new SlashCommandOptionBuilder()
                    .WithName("count")
                    .WithDescription("How many times (visual only)")
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithRequired(false)
                    .WithMinValue(1)
                    .WithMaxValue(20));
            }

            await guild.CreateApplicationCommandAsync(builder.Build());
        }

        await new SupportPanelSender().SendSupportPanelAsync(_client);
        Console.WriteLine("✅ Commands registered and support panel sent");

        await StartFiveMUpdater(guildId);

    }

    private async Task StartFiveMUpdater(ulong guildId)
    {
        string? fivemUrl = _config["FiveM:ServerUrl"];
        string? channelIdStr = _config["FiveM:ChannelId"];

        if (string.IsNullOrWhiteSpace(fivemUrl) || !ulong.TryParse(channelIdStr, out ulong channelId))
        {
            await Log(new LogMessage(LogSeverity.Warning, "FiveM", "⚠️ FiveM URL or ChannelId missing in appsettings.json"));
            return;
        }

        _fivemUpdater = new FiveMChannelUpdater(_client!, fivemUrl, guildId, channelId, Log);
        _fivemUpdater.Start();
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        if (_commandHandler != null)
            await _commandHandler.HandleCommandAsync(command);
    }

    private async Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());

        if (msg.Severity == LogSeverity.Info || msg.Severity == LogSeverity.Warning ||
            msg.Severity == LogSeverity.Error || msg.Severity == LogSeverity.Critical)
        {
            var channel = _client?.GetChannel(_logChannelId) as IMessageChannel;
            if (channel != null)
            {
                try
                {
                    var embed = new EmbedBuilder()
                        .WithTitle($"🛠️ {msg.Severity} Log")
                        .WithDescription($"```{msg.ToString()}```")
                        .WithColor(GetColorForSeverity(msg.Severity))
                        .WithFooter(footer => footer.Text = $"Source: {msg.Source}")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .WithThumbnailUrl("https://i.ibb.co/M5Qs7SgK/Logo-Copy.png")
                        .Build();

                    await channel.SendMessageAsync(embed: embed);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to send log to Discord: {ex.Message}");
                }
            }
        }
    }

    private Color GetColorForSeverity(LogSeverity severity)
    {
        return severity switch
        {
            LogSeverity.Critical => Color.DarkRed,
            LogSeverity.Error => Color.Red,
            LogSeverity.Warning => Color.Orange,
            LogSeverity.Info => Color.Blue,
            LogSeverity.Verbose => Color.LightGrey,
            LogSeverity.Debug => Color.DarkGrey,
            _ => Color.Default,
        };
    }
}
