using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class AiSupportService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public AiSupportService(IConfiguration config)
    {
        _http = new HttpClient();

        _apiKey = config["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new System.Exception("Gemini API key missing in appsettings.json");

        _model = config["Gemini:Model"] ?? "gemini-1.5-flash";
    }

    public async Task<string> GetSupportReplyAsync(
        string issue,
        string attemptedFixes = null,
        string ticketType = "FiveM Support")
    {
        var prompt = $"""
You are a professional FiveM server support assistant.

RULES:
- Only help with technical issues.
- Never discuss bans or staff actions.
- Do not guess.
- Provide step-by-step troubleshooting.

Ticket Type: {ticketType}

Player Issue:
{issue}

Fixes Tried:
{attemptedFixes ?? "None"}
""";

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body);

        var response = await _http.PostAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        var resultJson = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(resultJson);

        string reply = null;

        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];

            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var text))
            {
                reply = text.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            reply =
                "I'm unable to analyze this issue automatically right now. " +
                "A staff member will assist you shortly.";
        }


        return $"""
🤖 **FiveM Support Assistant**

{reply}

*If this does not resolve your issue, a staff member will assist you shortly.*
""";
    }
}
