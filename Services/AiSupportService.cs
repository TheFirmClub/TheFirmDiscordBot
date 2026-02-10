using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Threading.Tasks;

public class AiSupportService
{
    private readonly Client _client;
    private readonly string _model;

    public AiSupportService(IConfiguration config)
    {
        var apiKey = config["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exception("Gemini API key missing");

        _client = new Client(apiKey: apiKey);

        _model = config["Gemini:Model"] ?? "gemini-2.5-flash";
    }

    public async Task<string> GetSupportReplyAsync(
        string issue,
        string attemptedFixes = null,
        string ticketType = "FiveM Support")
    {
        var prompt = $"""
                      You are an expert FiveM server technical support assistant.

                      Provide clear, step-by-step troubleshooting.

                      If unsure, recommend contacting staff.

                      Ticket Type: {ticketType}

                      Issue:
                      {issue}

                      Fixes Tried:
                      {attemptedFixes ?? "None"}
                      """;

        var response = await _client.Models.GenerateContentAsync(
            model: _model,
            contents: prompt
        );

        var reply =
            response?.Candidates?[0]?.Content?.Parts?[0]?.Text
            ?? "I'm unable to analyze this issue right now. A staff member will assist you shortly.";

        return $"""
                🤖 **FiveM Support Assistant**

                {reply}

                *If this does not resolve your issue, a staff member will assist you shortly.*
                """;
    }
}