using Discord.WebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using FirmDiscordBot.Commands;

public class SlashCommandHandler
{
    private readonly Dictionary<string, ISlashCommand> _commands = new();
    private readonly IConfiguration _config;

    public SlashCommandHandler(
        IConfiguration config,
        InviteTrackerService inviteTracker,
        SuggestionsCommand suggestionsCommand)

    
    {
        _config = config;
        
        _commands.Add("ticketclose", new TicketCloseCommand(_config));
        
        var feedback = new FeedbackCommand();
        _commands.Add(feedback.Name, feedback);
        
        var joinCommand = new JoinCommand();
        _commands.Add(joinCommand.Name, joinCommand);

        var ForumLinkCommand = new ForumLinkCommand();
        _commands.Add(ForumLinkCommand.Name, ForumLinkCommand);

        var clearCacheCommand = new ClearCacheCommand();
        _commands.Add(clearCacheCommand.Name, clearCacheCommand);

        var cleanupCommand = new CleanupCommand();
        _commands.Add(cleanupCommand.Name, cleanupCommand);

        var support = new SupportCommand();
        _commands.Add(support.Name, support);

        _commands.Add("ticketclaim", new TicketClaimCommand());
        _commands.Add("ticketrelease", new TicketReleaseCommand());
        _commands.Add("ticketadduser", new TicketAddUserCommand());
        _commands.Add("ticketaddrole", new TicketAddRoleCommand());
        _commands.Add("ticketresolve", new TicketResolveCommand());
        _commands.Add("ticketrestrict", new TicketRestrictCommand());
        _commands.Add("tempticket", new TempTicketCommand());
        _commands.Add("modticket", new ModTicketCommand());
        _commands.Add("commandticket", new CommandTicketCommand());
        _commands.Add("addrole", new AddRoleCommand());
        _commands.Add("removerole", new RemoveRoleCommand());
        _commands.Add("checkrole", new CheckRoleCommand());
        _commands.Add("listroles", new ListRolesCommand());
        
        _commands.Add("meme", new MemeCommand());
        _commands.Add("coinflip", new CoinFlipCommand());
        _commands.Add("8ball", new Magic8BallCommand());
        _commands.Add("tictactoe", new TicTacToeCommand());
        _commands.Add("slap", new SlapCommand());
        _commands.Add("rps", new RpsCommand());
        _commands.Add("bomb", new BombDefuseCommand());
        _commands.Add("trivia", new TriviaCommand());
        _commands.Add("rickroll", new RickrollCommand());
        _commands.Add("fakeban", new FakeBanCommand());
        _commands.Add("screamer", new ScreamerCommand());
        _commands.Add("reverse", new ReverseCommand());
        _commands.Add("tinytext", new TinyTextCommand());
        _commands.Add("lag", new LagCommand());
        _commands.Add("sus", new SusCommand());
        _commands.Add("hack", new HackCommand());
        _commands.Add("cursedimage", new CursedImageCommand());
        _commands.Add("loud", new LoudCommand());
        _commands.Add("hangman", new HangmanCommand());
        _commands.Add("socials", new SocialsCommand());
        _commands.Add("playtime", new PlaytimeCommand());
        _commands.Add("resetplaytime", new ResetPlaytimeCommand());
        _commands.Add("game", new GameModCommands());
        _commands.Add("gamestats", new GameStatsCommand());
        _commands.Add("mdtincidents", new MDTIncidentsCommand());
        _commands.Add("listinv", new ListInventoryCommand());
        _commands.Add("sendtfuapp", new SendTFUAppCommand());
        
        
        // --- NEW: invite tracker commands ---
        _commands.Add("myinvites",  new MyInvitesCommand(inviteTracker));
        _commands.Add("topinvites", new TopInvitesCommand(inviteTracker));
        _commands.Add("invitecodes",new InviteCodesCommand(inviteTracker));
        _commands.Add("clearinvites", new ClearInvitesCommand(inviteTracker));

        // --- NEW: /preban ---
        var preban = new PrebanCommand();
        _commands.Add(preban.Name, preban);
        
        var playtimeCommands = new CheckPlaytimeCommands();
        _commands.Add("myplaytime", playtimeCommands);
        _commands.Add("checkplaytime", playtimeCommands);
        
        // --- NEW: police blacklist ---
        var policeBlacklist = new PoliceBlacklistCommand();
        _commands.Add(policeBlacklist.Name, policeBlacklist);

        var tfhsBlacklist = new TFHSBlacklistCommand();
        _commands.Add(tfhsBlacklist.Name, tfhsBlacklist);
        
        var spcStashClear = new SpcStashClearCommand();
        _commands.Add(spcStashClear.Name, spcStashClear);
        
        // --- NEW: manual verification ---
        _commands.Add("manualverify", new ManualVerifyCommand());
        _commands.Add("userverified", new UserVerifiedCommand());
        _commands.Add("userverifytimeout", new UserVerifyTimeoutCommand());
        _commands.Add("userverifydenied", new UserVerifyDeniedCommand());

    }

    public async Task HandleCommandAsync(SocketSlashCommand command)
    {
        if (_commands.TryGetValue(command.CommandName, out var handler))
        {
            await handler.ExecuteAsync(command);
        }
        else
        {
            await command.RespondAsync("Command not recognized.");
        }
    }

    public IEnumerable<ISlashCommand> GetAllCommands() => _commands.Values;
}
