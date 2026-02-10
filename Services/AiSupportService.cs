using OpenAI.Chat;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading.Tasks;

#region Settings Model
public class OpenAiSettings
{
    public string ApiKey { get; set; }
    public string Model { get; set; }
}
#endregion

public class AiSupportService
{
    private readonly ChatClient _chatClient;

    public AiSupportService(IConfiguration configuration)
    {
        // Read settings from appsettings.json
        var settings = new OpenAiSettings();
        configuration.GetSection("OpenAI").Bind(settings);

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new System.Exception("OpenAI:ApiKey is missing from appsettings.json");

        _chatClient = new ChatClient(
            model: settings.Model ?? "gpt-4.1-mini",
            apiKey: settings.ApiKey
        );
    }

    public async Task<string> GetSupportReplyAsync(
        string issue,
        string attemptedFixes = null,
        string ticketType = "FiveM Support")
    {
        var systemPrompt = """
You are a professional FiveM roleplay server Support Assistant.

YOUR ROLE:
Help players troubleshoot FiveM technical issues such as crashes, bugs,
connection problems, and installation issues.

STRICT RULES:
- Do NOT handle bans, punishments, staff actions, or rule disputes.
- Do NOT invent server rules or policies.
- Do NOT guess.
- If unsure, tell the player a staff member will assist shortly.
- Never provide exploits or bypass methods.
- Keep responses structured and easy to follow.

ALLOWED TOPICS:
• FiveM crashes
• Cache issues
• Resource loading problems
• GTA V / FiveM installation issues
• Connection & timeout errors
• Client-side bugs
""";

        var userPrompt = $"""
Ticket Type: {ticketType}

Player Issue:
{issue}

Troubleshooting Already Tried:
{attemptedFixes ?? "None provided"}
""";

        var response = await _chatClient.CompleteChatAsync(
            new ChatMessage[]
            {
                ChatMessage.CreateSystemMessage(systemPrompt),
                ChatMessage.CreateUserMessage(userPrompt)
            },
            new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500
            }
        );

        var reply = string.Join(
            "\n",
            response.Value.Content
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .Select(c => c.Text));

        return FormatForDiscord(reply);
    }

    private string FormatForDiscord(string text)
    {
        return
$"""
🤖 **FiveM Support Assistant**

{text}

*If this does not resolve your issue, a staff member will assist you shortly.*
""";
    }
}
