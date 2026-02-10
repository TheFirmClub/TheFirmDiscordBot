using Discord;
using Discord.WebSocket;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;
using System.Text.RegularExpressions;

public class TicketActivityService
{
    private readonly DiscordSocketClient _client;
    private readonly SheetsService _sheets;
    private readonly HashSet<ulong> _processedMessages = new();

    private const ulong SupportLogsChannelId = 1394405064520499415;
    private const string SpreadsheetId = "1GvPht9ETF-JwkX3NO2S9tWFOiLWCBsMraDfGWM_PRsk";
    private const string SheetName = "Sheet1";

    public TicketActivityService(DiscordSocketClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessage;

        _sheets = CreateSheetsService().GetAwaiter().GetResult();
    }

    // ================= GOOGLE OAUTH =================

    private async Task<SheetsService> CreateSheetsService()
    {
        using var stream =
            new FileStream("client_secret.json", FileMode.Open, FileAccess.Read);

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            new[] { SheetsService.Scope.Spreadsheets },
            "user",
            CancellationToken.None,
            new FileDataStore("token", true));

        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Discord Ticket Logger"
        });
    }

    // ================= MESSAGE HANDLER =================

    private async Task OnMessage(SocketMessage msg)
{
    if (msg.Channel.Id != SupportLogsChannelId)
        return;

    if (msg.Embeds.Count == 0)
        return;

    // ✅ Prevent duplicate logs after reconnects
    if (!_processedMessages.Add(msg.Id))
        return;

    var embed = msg.Embeds.First();
    
    // ================= DEBUG (TEMPORARY) =================
    Console.WriteLine("==== EMBED DUMP START ====");
    Console.WriteLine($"TITLE: {embed.Title}");
    Console.WriteLine($"DESC: {embed.Description}");
    Console.WriteLine($"FOOTER: {embed.Footer?.Text}");

    foreach (var field in embed.Fields)
    {
        Console.WriteLine($"FIELD NAME: {field.Name}");
        Console.WriteLine($"FIELD VALUE: {field.Value}");
    }
    Console.WriteLine("==== EMBED DUMP END ====");
    // =====================================================

    var action = DetectAction(embed.Title ?? "");
    if (action == null)
        return;

    string moderatorName = "Unknown";
    string moderatorId = "Unknown";
    string channelName = "Unknown";
    string channelId = "Unknown";

    foreach (var field in embed.Fields)
    {
        // ================= MODERATOR =================
        if (field.Name.Contains("By", StringComparison.OrdinalIgnoreCase) ||
            field.Name.Contains("Moderator", StringComparison.OrdinalIgnoreCase))
        {
            var idMatch = Regex.Match(field.Value, @"\d{17,20}");

            if (idMatch.Success)
            {
                moderatorId = idMatch.Value;

                if (ulong.TryParse(moderatorId, out var uid))
                {
                    IUser? user = _client.GetUser(uid);

                    if (user == null)
                        user = await _client.Rest.GetUserAsync(uid);

                    // ⭐ BEST PRACTICE — store mention instead
                    moderatorName = $"<@{moderatorId}>";
                }
            }
        }

        // ================= CHANNEL =================
        if (field.Name.Contains("Channel", StringComparison.OrdinalIgnoreCase) ||
            field.Name.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
        {
            // Extract ID (works even with backticks)
            var idMatch = Regex.Match(field.Value, @"\d{17,20}");
            if (idMatch.Success)
                channelId = idMatch.Value;

            // Extract channel name
            var nameMatch = Regex.Match(field.Value, @"^(.+?)\s*\(");
            if (nameMatch.Success)
                channelName = nameMatch.Groups[1].Value.Trim();
        }
    }

    await AppendRow(new List<object>
    {
        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        moderatorName,
        moderatorId,
        action,
        channelName,
        channelId
    });

    Console.WriteLine($"✅ Logged {action} by {moderatorName} in {channelName}");
}


    // ================= GOOGLE SHEETS =================

    private async Task AppendRow(List<object> row)
    {
        var range = new ValueRange
        {
            Values = new List<IList<object>> { row }
        };

        var request = _sheets.Spreadsheets.Values.Append(
            range,
            SpreadsheetId,
            $"{SheetName}!A:F");

        request.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await request.ExecuteAsync();
    }

    // ================= PARSING =================

    private string? DetectAction(string text)
    {
        if (text.Contains("Ticket Claimed", StringComparison.OrdinalIgnoreCase))
            return "Claimed";

        if (text.Contains("Ticket Resolved", StringComparison.OrdinalIgnoreCase))
            return "Resolved";

        if (text.Contains("Ticket Closed", StringComparison.OrdinalIgnoreCase))
            return "Closed";

        return null;
    }

    private string ExtractDiscordId(string text)
    {
        var match = Regex.Match(text, @"\((\d{17,20})\)");
        return match.Success ? match.Groups[1].Value : "Unknown";
    }

    private string GetUsername(string discordId)
    {
        if (!ulong.TryParse(discordId, out var id))
            return "Unknown";

        var user = _client.GetUser(id);
        return user?.Username ?? "Unknown";
    }

    private (string, string) ExtractChannel(string text)
    {
        var match = Regex.Match(text, @"([a-zA-Z\-]+-\d+)\s*\((\d{17,20})\)");

        if (!match.Success)
            return ("Unknown", "Unknown");

        return (
            match.Groups[1].Value,
            match.Groups[2].Value
        );
    }
}
