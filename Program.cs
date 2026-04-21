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
    private SuggestionsCommand _suggestionsCommand = new SuggestionsCommand();
    private CheckRoleInfoCommand _checkRoleInfoCommand = new CheckRoleInfoCommand();
    private FirmDiscordBot.Services.ManualVerifyOnJoinHandler? _manualVerifyOnJoin;
    private FeedbackCommand _feedbackCommand = new FeedbackCommand();
    private SpcStashClearCommand _spcStashClear = new SpcStashClearCommand();

    private ulong _logChannelId = 1394449608603603085;

    // 🔹 FiveM integration
    private FiveMChannelUpdater? _fivemUpdater;

    private Mee6LogForwarder? _mee6Forwarder;

    private ChangelogTrackerService? _changelogTracker;
    
    private TerritoryAlertService? _territoryAlert;
    
    private BodycamVideoForwardService? _bodycamVideoForward;
    
    private TicketActivityService? _ticketActivity;
    
    private EvidenceRelayService? _evidenceRelay;
    
    private VoiceModLogger? _voiceLogger;
    
    private AiSupportService? _aiSupport;
    
    private SupportModalHandler? _supportModalHandler;

    // ✅ LOA command integration
    private StaffLoaCommand _staffLoa = new StaffLoaCommand();
    private bool _staffLoaRegistered = false;

    public static Task Main(string[] args) => new Program().MainAsync();

    public async Task MainAsync()
    {
        _config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        _aiSupport = new AiSupportService(_config);

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
       
        _manualVerifyOnJoin = new FirmDiscordBot.Services.ManualVerifyOnJoinHandler(_client);
        _manualVerifyOnJoin.Register();
        
        // ⭐ ADD THIS RIGHT HERE
        _client.MessageReceived += (msg) =>
        {
            Console.WriteLine($"GLOBAL MESSAGE EVENT → Channel:{msg.Channel.Id} Author:{msg.Author}");
            return Task.CompletedTask;
        };

        _client.Log += Log;
        _client.Ready += async () => await ReadyAsync(guildId);

        // new StoreEmbed(_client);
        _mee6Forwarder = new Mee6LogForwarder(_client);

        // after _client is created
        _inviteTracker = new InviteTrackerService(_client);
        await _inviteTracker.InitializeAsync();

        // Changelog tracker (listens in changelog channel and posts stats)
        _changelogTracker = new ChangelogTrackerService(_client!);
        
        // ✅ Turf invasion detection
        _territoryAlert = new TerritoryAlertService(_client!);
        
        _bodycamVideoForward = new BodycamVideoForwardService(_client!);
        
        _ticketActivity = new TicketActivityService(_client!);
        
        _commandHandler = new SlashCommandHandler(
            _config,
            _inviteTracker,
            _suggestionsCommand
        );
        
        _evidenceRelay = new EvidenceRelayService(_client);
        

        _ticketButtonHandler = new TicketButtonHandler(_config, _aiSupport);
        _supportModalHandler = new SupportModalHandler(_config, _aiSupport);
        _client.ModalSubmitted += _supportModalHandler.HandleModalAsync;
        _client.SlashCommandExecuted += SlashCommandExecuted;
        _client.SelectMenuExecuted += _supportMenuHandler.HandleAsync;
        _client.AutocompleteExecuted += _checkRoleInfoCommand.HandleAutocompleteAsync;
        _client.ButtonExecuted += async component =>
        {
            if (component.Data.CustomId.StartsWith("mdt_"))
            {
                await MDTIncidentsCommand.HandleButton(component);
                return;
            }

            if (component.Data.CustomId.StartsWith("listinv_"))
            {
                await ListInventoryCommand.HandleButton(component);
                return;
            }

            // ✅ CheckRoleInfo pagination buttons first
            if (component.Data.CustomId.StartsWith("roleinfo_"))
            {
                await _checkRoleInfoCommand.HandleButton(component);
                return;
            }

            // existing handlers
            await _ticketButtonHandler.HandleAsync(component);
            await TicTacToeCommand.HandleButton(component);
            await RpsCommand.HandleComponentAsync(component);
            await BombDefuseCommand.HandleComponentAsync(component);
            await TriviaCommand.HandleButton(component);
            await HangmanCommand.HandleButtonAsync(component);
        };

        ulong roleLogChannelId = 1393726185804005497;
        _roleLogger = new RoleLogger(_client, roleLogChannelId);

        ulong modNotesChannelId = 1394451583709745273; // your mod notes channel
        _voiceLogger = new VoiceModLogger(_client, modNotesChannelId);
        
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
        
        var existingCommands = await guild.GetApplicationCommandsAsync();
        foreach (var cmd in existingCommands)
        {
            if (cmd.Name == "gamemod")
            {
                await cmd.DeleteAsync();
                Console.WriteLine("🗑️ Removed old /gamemod");
            }
            
            if (cmd.Name == "policeblacklist")
            {
                await cmd.DeleteAsync();
                Console.WriteLine("🗑️ Removed old /policeblacklist");
            }
            
            if (cmd.Name == "tfhsblacklist")
            {
                await cmd.DeleteAsync();
                Console.WriteLine("🗑️ Removed old /tfhsblacklist");
            }
            
            if (cmd.Name == "spcstashclear")
            {
                await cmd.DeleteAsync();
                Console.WriteLine("🗑️ Removed old /spcstashclear");
            }
            
        }

        // ✅ Register LOA commands once (handles /staffloa, /loaremove, /staffloalist)
        if (!_staffLoaRegistered)
        {
            await _staffLoa.RegisterAsync(_client);
            _staffLoaRegistered = true;
            Console.WriteLine("✅ Registered LOA commands (/staffloa, /loaremove, /staffloalist)");
        }
        
        // 🔥 DELETE OLD /suggestions COMMAND FIRST
        var commands = await guild.GetApplicationCommandsAsync();

        foreach (var cmd in commands)
        {
            if (cmd.Name == "suggestions")
            {
                await cmd.DeleteAsync();
                Console.WriteLine("🗑️ Removed old /suggestions");
            }
        }
        
        foreach (var cmd in await guild.GetApplicationCommandsAsync())
        {
            if (cmd.Name == "feedback")
                await cmd.DeleteAsync();
        }

        // ✅ Register Suggestions (fresh)
        await _suggestionsCommand.RegisterAsync(_client);
        Console.WriteLine("✅ Registered /suggestions");
        
        await _feedbackCommand.RegisterAsync(_client);
        Console.WriteLine("✅ Registered /feedback");
        
        // ✅ Register /playtime (with subcommands) BEFORE the foreach
        try
        {
            var playtimeCmd = new SlashCommandBuilder()
                .WithName("playtime")
                .WithDescription("Check police or ambulance playtime")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("police")
                    .WithDescription("Check police playtime for a CID")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("cid", ApplicationCommandOptionType.String, "Citizen ID", true))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("ambulance")
                    .WithDescription("Check ambulance playtime for a CID")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("cid", ApplicationCommandOptionType.String, "Citizen ID", true));

            await guild.CreateApplicationCommandAsync(playtimeCmd.Build());
            Console.WriteLine("✅ /playtime registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /playtime: {ex}");
        }

        // ✅ Register /resetplaytime (with subcommands)
        try
        {
            var resetPlaytimeCmd = new SlashCommandBuilder()
                .WithName("resetplaytime")
                .WithDescription("Reset police or ambulance playtime by CID")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("police")
                    .WithDescription("Reset police playtime for a CID")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("cid", ApplicationCommandOptionType.String, "Citizen ID", true))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("ambulance")
                    .WithDescription("Reset ambulance playtime for a CID")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("cid", ApplicationCommandOptionType.String, "Citizen ID", true));

            await guild.CreateApplicationCommandAsync(resetPlaytimeCmd.Build());
            Console.WriteLine("✅ /resetplaytime registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /resetplaytime: {ex}");
        }

        // -------------------------------
        // Register /listinv (Inventory Lookup)
        // -------------------------------
        try
        {
            var listInvCmd = new SlashCommandBuilder()
                .WithName("listinv")
                .WithDescription("List all players who have a specific inventory item")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("item")
                    .WithDescription("Item name (e.g. weapon_pistol, radio, ammo-9)")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                );

            await guild.CreateApplicationCommandAsync(listInvCmd.Build());
            Console.WriteLine("✅ /listinv registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /listinv: {ex}");
        }

        // ✅ Register /checkroleinfo (with autocomplete)
        try
        {
            var roleInfoCmd = new SlashCommandBuilder()
                .WithName("checkroleinfo")
                .WithDescription("View members of a specific role")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("role")
                    .WithDescription("Role name or ID to view")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .WithAutocomplete(true));

            await guild.CreateApplicationCommandAsync(roleInfoCmd.Build());
            Console.WriteLine("✅ /checkroleinfo registered");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to register /checkroleinfo: {ex}");
        }

        // Register /gamestats
        try
        {
            var gamestatsCommand = new SlashCommandBuilder()
                .WithName("gamestats")
                .WithDescription("Shows server-wide game statistics (Senior Management only)");

            await guild.CreateApplicationCommandAsync(gamestatsCommand.Build());
            Console.WriteLine("✅ /gamestats registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /gamestats: {ex}");
        }
        
        try
        {
            var stashClearCmd = new SlashCommandBuilder()
                .WithName("spcstashclear")
                .WithDescription("Clear SPC stash metadata (spc-stash & spc-stash2)");

            await guild.CreateApplicationCommandAsync(stashClearCmd.Build());
            Console.WriteLine("✅ /spcstashclear registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /spcstashclear: {ex}");
        }

        // -------------------------------
        // Register /mdtincidents (Police Command only)
        // -------------------------------
        try
        {
            var mdtIncidentsCmd = new SlashCommandBuilder()
                .WithName("mdtincidents")
                .WithDescription("View MDT incidents by time range (Police Command only)")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("range")
                    .WithDescription("Time range to query")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .AddChoice("Day", "day")
                    .AddChoice("Week", "week")
                    .AddChoice("Month", "month")
                );

            await guild.CreateApplicationCommandAsync(mdtIncidentsCmd.Build());
            Console.WriteLine("✅ /mdtincidents registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /mdtincidents: {ex}");
        }
        
        // -------------------------------
        // Register /policeblacklist (SUBCOMMANDS)
        // -------------------------------
        try
        {
            var policeBlacklistCmd = new SlashCommandBuilder()
                .WithName("policeblacklist")
                .WithDescription("Manage police blacklist")

                // /policeblacklist add <cid> <days|PERM> <grade?>
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("add")
                    .WithDescription("Add a police blacklist")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("citizenid",
                        ApplicationCommandOptionType.String,
                        "Citizen ID (e.g. HEV80184)",
                        true)
                    .AddOption("days",
                        ApplicationCommandOptionType.String,
                        "Number of days or PERM",
                        true)
                    .AddOption("grade",
                        ApplicationCommandOptionType.Integer,
                        "Max police grade allowed (omit for full ban)",
                        false)
                )

                // /policeblacklist remove <cid>
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("remove")
                    .WithDescription("Remove a police blacklist")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("citizenid",
                        ApplicationCommandOptionType.String,
                        "Citizen ID",
                        true)
                );

            await guild.CreateApplicationCommandAsync(policeBlacklistCmd.Build());
            Console.WriteLine("✅ /policeblacklist (add/remove) registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /policeblacklist: {ex}");
        }
        
        // -------------------------------
        // Register /tfhsblacklist (SUBCOMMANDS)
        // -------------------------------
        try
        {
            var tfhsBlacklistCmd = new SlashCommandBuilder()
                .WithName("tfhsblacklist")
                .WithDescription("Manage TFHS blacklist")

                // /tfhsblacklist add <cid> <days|PERM> <grade?>
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("add")
                    .WithDescription("Add a TFHS blacklist")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("citizenid",
                        ApplicationCommandOptionType.String,
                        "Citizen ID (e.g. HEV80184)",
                        true)
                    .AddOption("days",
                        ApplicationCommandOptionType.String,
                        "Number of days or PERM",
                        true)
                    .AddOption("grade",
                        ApplicationCommandOptionType.Integer,
                        "Max ambulance grade allowed (omit for full ban)",
                        false)
                )

                // /tfhsblacklist remove <cid>
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("remove")
                    .WithDescription("Remove a ambulance blacklist")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("citizenid",
                        ApplicationCommandOptionType.String,
                        "Citizen ID",
                        true)
                );

            await guild.CreateApplicationCommandAsync(tfhsBlacklistCmd.Build());
            Console.WriteLine("✅ /tfhsblacklist (add/remove) registered");
        }
        catch (Discord.Net.HttpException ex)
        {
            Console.WriteLine($"❌ Failed to register /tfhsblacklist: {ex}");
        }


        // ---------------------------------------------
        // REGISTER STAFF + PUBLIC PLAYTIME COMMANDS
        // ---------------------------------------------

        // /myplaytime
        await guild.CreateApplicationCommandAsync(
            new SlashCommandBuilder()
                .WithName("myplaytime")
                .WithDescription("Show your own playtime")
                .Build()
        );

        // /checkplaytime <discordid>
        await guild.CreateApplicationCommandAsync(
            new SlashCommandBuilder()
                .WithName("checkplaytime")
                .WithDescription("Staff lookup of anyone's playtime")
                .AddOption("discordid", ApplicationCommandOptionType.String, "Discord ID to look up", true)
                .Build()
        );

        Console.WriteLine("✅ Registered /myplaytime + /checkplaytime");
        
        // 🔥 DELETE OLD /sendtfuapp FIRST (FIXES YOUR ERROR)
        var cmds = await guild.GetApplicationCommandsAsync();

        foreach (var cmd in cmds)
        {
            if (cmd.Name == "sendtfuapp")
            {
                await cmd.DeleteAsync();
                Console.WriteLine("🗑️ Removed old /sendtfuapp");
            }
        }
      
        // Register your other existing commands from SlashCommandHandler
        foreach (var command in _commandHandler!.GetAllCommands())
        {
            if (command.Name.Equals("policeblacklist", StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (command.Name.Equals("tfhsblacklist", StringComparison.OrdinalIgnoreCase))
                continue;
            
            // ⛔ Skip playtime here because it was registered manually as subcommands
            if (command.Name.Equals("playtime", StringComparison.OrdinalIgnoreCase) ||
                command.Name.Equals("mdtincidents", StringComparison.OrdinalIgnoreCase) ||
                command.Name.Equals("resetplaytime", StringComparison.OrdinalIgnoreCase) ||
                command.Name.Equals("listinv", StringComparison.OrdinalIgnoreCase))
                continue;
            
            // ✅ Special handling for gamemod because it uses subcommands
            if (command is GameModCommands gm)
            {
                await guild.CreateApplicationCommandAsync(gm.Build());
                Console.WriteLine("✅ /game registered");
                continue; // Skip normal registration below
            }
            
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
                builder.AddOption("user", ApplicationCommandOptionType.User, "Create a temporary ticket for", true);
            else if (command.Name == "modticket")
                builder.AddOption("user", ApplicationCommandOptionType.User, "Create a Moderation Ticket for", true);
            else if (command.Name == "commandticket")
                builder.AddOption("user", ApplicationCommandOptionType.User, "Create a Command Team Ticket for", true);
            else if (command.Name == "addrole" || command.Name == "removerole")
            {
                builder.AddOption("user", ApplicationCommandOptionType.User, "Target user", true);
                builder.AddOption("role", ApplicationCommandOptionType.Role, "Role to assign/remove", true);
            }
            else if (command.Name == "sendtfuapp")
            {
                builder.AddOption("user", ApplicationCommandOptionType.User, "User to send application to", true);
            }
            else if (command.Name == "meme")
            {
                builder.AddOption("url", ApplicationCommandOptionType.String, "Direct image link (jpg/png)", true);
                builder.AddOption("top", ApplicationCommandOptionType.String, "Top text", true);
                builder.AddOption("bottom", ApplicationCommandOptionType.String, "Bottom text", true);
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
            else if (command.Name == "bomb")
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
            else if (command.Name == "manualverify")
                builder.AddOption("discordid", ApplicationCommandOptionType.String, "Discord ID of the user", true);
            else if (command.Name == "cursedimage")
            {
                // no options
            }
            else if (command.Name == "hangman")
            {
                // no options for now
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
            else if (command.Name == "preban")
            {
                builder.WithDefaultMemberPermissions(GuildPermission.BanMembers);
                builder.AddOption("userid", ApplicationCommandOptionType.String, "Discord user ID to pre-ban", true);
                builder.AddOption("reason", ApplicationCommandOptionType.String, "Reason for the ban", false);
            }

            await guild.CreateApplicationCommandAsync(builder.Build());
        }

        await new SupportPanelSender().SendSupportPanelAsync(_client);
        Console.WriteLine("✅ Commands registered and support panel sent");

        await StartFiveMUpdater(guildId);
    }

    private async Task StartFiveMUpdater(ulong guildId)
    {
        if (_fivemUpdater != null)
        {
            await Log(new LogMessage(LogSeverity.Info, "FiveM", "ℹ️ FiveM updater already running, skipping."));
            return;
        }

        string? fivemUrl = _config["FiveM:ServerUrl"];
        string? channelIdStr = _config["FiveM:ChannelId"];

        if (string.IsNullOrWhiteSpace(fivemUrl) || !ulong.TryParse(channelIdStr, out ulong channelId))
        {
            await Log(new LogMessage(LogSeverity.Warning, "FiveM", "⚠️ FiveM URL or ChannelId missing in appsettings.json"));
            return;
        }

        _fivemUpdater = new FiveMChannelUpdater(_client!, fivemUrl, guildId, channelId, Log);
        _fivemUpdater.Start();

        await Log(new LogMessage(LogSeverity.Info, "FiveM", "✅ FiveM updater started"));
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        var name = command.Data.Name?.ToLowerInvariant();

        // 🔥 FIX: handle suggestions FIRST
        if (name == "suggestions")
        {
            await _suggestionsCommand.ExecuteAsync(command);
            return;
        }
        
        if (name == "feedback")
        {
            await _feedbackCommand.ExecuteAsync(command);
            return;
        }

        // ✅ Route LOA commands
        if (name == "staffloa" || name == "loaremove" || name == "staffloalist")
        {
            await _staffLoa.ExecuteAsync(command);
            return;
        }

        // ✅ Route CheckRoleInfo
        if (name == "checkroleinfo")
        {
            await _checkRoleInfoCommand.ExecuteAsync(command);
            return;
        }
        
        if (name == "spcstashclear")
        {
            await _spcStashClear.ExecuteAsync(command);
            return;
        }

        // fallback to handler
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
