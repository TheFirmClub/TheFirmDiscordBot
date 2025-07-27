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

    private ulong _logChannelId = 1394449608603603085;

    public static Task Main(string[] args) => new Program().MainAsync();

    public async Task MainAsync()
    {
        // Load config
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
        _client.SlashCommandExecuted += SlashCommandExecuted;

        _commandHandler = new SlashCommandHandler();
        // Correct channel for role logs
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

            await guild.CreateApplicationCommandAsync(builder.Build());
        }

        Console.WriteLine("✅ Commands registered");
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
