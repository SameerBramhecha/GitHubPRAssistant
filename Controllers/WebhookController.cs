using GitHubPRAssistant.Agents;
using GitHubPRAssistant.Services;
using GitHubPRAssistant.Models;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitHubPRAssistant.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly PrReviewAgent _agent;
    private readonly GitHubService _githubService;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        PrReviewAgent agent,
        GitHubService githubService,
        IConfiguration config,
        ILogger<WebhookController> logger)
    {
        _agent = agent;
        _githubService = githubService;
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook()
    {
        try
        {
            // Read the raw body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // Verify webhook signature (if configured)
            if (!VerifySignature(body))
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized("Invalid signature");
            }

            // Parse JSON payload
            var payload = JsonDocument.Parse(body);
            var root = payload.RootElement;

            // Check event type
            var eventType = Request.Headers["X-GitHub-Event"].ToString();
            _logger.LogInformation("Received GitHub event: {EventType}", eventType);

            if (eventType != "pull_request")
            {
                _logger.LogInformation("Ignoring non-PR event: {EventType}", eventType);
                return Ok(new { message = "Event ignored" });
            }

            // Check action type
            var action = root.GetProperty("action").GetString();
            if (action != "opened" && action != "synchronize" && action != "reopened")
            {
                _logger.LogInformation("Ignoring PR action: {Action}", action);
                return Ok(new { message = "Action ignored" });
            }

            // Parse PR context
            var prContext = await _githubService.ParsePrWebHookAsync(root);
            _logger.LogInformation("Processing PR: {Owner}/{Repo} #{Number}",
                prContext.Owner, prContext.Repo, prContext.PrNumber);

            // Run agent review
            var result = await _agent.RunReviewAsync(prContext);

            // Post comment to GitHub
            await _githubService.PostPrCommentAsync(prContext, result.Comment);

            return Ok(new
            {
                success = true,
                prNumber = prContext.PrNumber,
                issuesFound = result.Issues.Count,
                hasCritical = result.HasCriticalIssues
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON payload");
            return BadRequest("Invalid JSON payload");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestEndpoint([FromBody] TestPayload test)
    {
        try
        {
            _logger.LogInformation("Test endpoint called for {Owner}/{Repo} PR #{Number}",
                test.Owner, test.Repo, test.PrNumber);

            // Create a minimal PR context for testing
            var prContext = new PRContext
            {
                Owner = test.Owner,
                Repo = test.Repo,
                PrNumber = test.PrNumber,
                PrTitle = "Test PR",
                PrDescription = "This is a test PR"
            };

            // Fetch files
            prContext.ChangedFiles = await _githubService.GetPrFilesAsync(prContext);

            // Run review
            var result = await _agent.RunReviewAsync(prContext);

            return Ok(new
            {
                success = true,
                summary = result.Summary,
                issuesFound = result.Issues.Count,
                issues = result.Issues,
                comment = result.Comment
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in test endpoint");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private bool VerifySignature(string payload)
    {
        var secret = _config["GitHub:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            // If no secret configured, skip verification (dev mode)
            _logger.LogWarning("Webhook secret not configured - skipping signature verification");
            return true;
        }

        var signatureHeader = Request.Headers["X-Hub-Signature-256"].ToString();
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        var signature = signatureHeader.Replace("sha256=", "");
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var expectedSignature = Convert.ToHexString(hash).ToLower();

        return signature == expectedSignature;
    }
}

public record TestPayload(string Owner, string Repo, int PrNumber);