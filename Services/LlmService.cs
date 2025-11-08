using OpenAI.Chat;
using System.ClientModel;

namespace GitHubPRAssistant.Services;

public class LlmService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<LlmService> _logger;
    private readonly string _model;

    public LlmService(IConfiguration config, ILogger<LlmService> logger)
    {
        _logger = logger;

        var apiKey = config["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI API key not configured");
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        // Create OpenAI client (not Azure)
        var credential = new ApiKeyCredential(apiKey);
        var client = new OpenAI.OpenAIClient(credential);
        _chatClient = client.GetChatClient(_model);
    }

    public async Task<string> GenerateAsync(string prompt, int maxTokens = 1500, double temperature = 0.7)
    {
        try
        {
            _logger.LogInformation("Calling LLM with {CharCount} characters", prompt.Length);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert code reviewer assistant. Provide clear, actionable, and constructive feedback."),
                new UserChatMessage(prompt)
            };

            var options = new ChatCompletionOptions
            {

                //MaxTokens = maxTokens,
                Temperature = (float)temperature
            };

            var completion = await _chatClient.CompleteChatAsync(messages, options);
            var response = completion.Value.Content[0].Text;

            _logger.LogInformation("LLM response received: {CharCount} characters", response.Length);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate LLM response");
            throw;
        }
    }

    public async Task<string> SummarizePrAsync(string title, string description, List<string> changedFiles)
    {
        var prompt = $@"Summarize this Pull Request in 3-5 concise bullet points. Focus on the intent and impact.

PR Title: {title}

PR Description: {description}

Changed Files:
{string.Join("\n", changedFiles.Take(20))}

Provide only the bullet points, no additional text.";

        return await GenerateAsync(prompt, maxTokens: 500, temperature: 0.5);
    }

    public async Task<string> GenerateReviewCommentAsync(string title, string description, string summary, List<string> issues)
    {
        var issuesText = issues.Any()
            ? string.Join("\n", issues.Select((i, idx) => $"{idx + 1}. {i}"))
            : "No issues found.";

        var prompt = $@"Write a constructive code review comment for this Pull Request.

PR Title: {title}
PR Description: {description}

PR Summary:
{summary}

Issues Found:
{issuesText}

Structure your response as:
1. **Summary** (1-2 sentences)
2. **Key Findings** (bullet points)
3. **Recommendations** (actionable bullet points)
4. **Checklist** (3-5 items for the author to verify)

Keep the tone friendly, specific, and actionable. Use markdown formatting.";

        return await GenerateAsync(prompt, maxTokens: 1500, temperature: 0.7);
    }
}