using GitHubPRAssistant.Models;
using GitHubPRAssistant.Services;

namespace GitHubPRAssistant.Tools;

public class PrSummarizerTool
{
    private readonly GitHubService _githubService;
    private readonly LlmService _llmService;
    private readonly ILogger<PrSummarizerTool> _logger;

    public PrSummarizerTool(
        GitHubService githubService,
        LlmService llmService,
        ILogger<PrSummarizerTool> logger)
    {
        _githubService = githubService;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<string> SummarizeAsync(PRContext context)
    {
        try
        {
            _logger.LogInformation("Starting PR summarization for PR #{Number}", context.PrNumber);

            // Get diff information
            var diffs = await _githubService.GetPrDiffsAsync(context);

            // Build a comprehensive summary of changes
            var changeSummary = BuildChangeSummary(context.ChangedFiles, diffs);

            // Use LLM to generate intelligent summary
            var summary = await _llmService.SummarizePrAsync(
                context.PrTitle,
                context.PrDescription,
                changeSummary
            );

            _logger.LogInformation("PR summarization completed");
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize PR");
            // Return a basic summary as fallback
            return GenerateFallbackSummary(context);
        }
    }

    private List<string> BuildChangeSummary(List<ChangedFile> files, List<string> diffs)
    {
        var summary = new List<string>();

        // Group files by type/extension
        var fileGroups = files.GroupBy(f => GetFileCategory(f.Path));

        foreach (var group in fileGroups)
        {
            var fileCount = group.Count();
            var totalAdded = group.Sum(f => f.Added);
            var totalRemoved = group.Sum(f => f.Removed);

            var categoryName = group.Key;
            summary.Add($"{categoryName} ({fileCount} files): +{totalAdded} -{totalRemoved} lines");

            // List individual files if not too many
            if (fileCount <= 5)
            {
                foreach (var file in group)
                {
                    summary.Add($"  - {file.Path} (+{file.Added} -{file.Removed}) [{file.Status}]");
                }
            }
            else
            {
                // Just show top 3 by changes
                var topFiles = group
                    .OrderByDescending(f => f.Added + f.Removed)
                    .Take(3);

                foreach (var file in topFiles)
                {
                    summary.Add($"  - {file.Path} (+{file.Added} -{file.Removed}) [{file.Status}]");
                }
                summary.Add($"  - ... and {fileCount - 3} more files");
            }
        }

        // Add statistics
        summary.Add("");
        summary.Add($"Total: {files.Count} files changed, " +
                   $"+{files.Sum(f => f.Added)} -{files.Sum(f => f.Removed)} lines");

        return summary;
    }

    private string GetFileCategory(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        var fileName = Path.GetFileName(filePath).ToLower();

        // Configuration files
        if (IsConfigFile(fileName, extension))
            return "Configuration";

        // Documentation
        if (IsDocumentationFile(fileName, extension))
            return "Documentation";

        // Tests
        if (IsTestFile(filePath, fileName))
            return "Tests";

        // Language-specific categorization
        return extension switch
        {
            ".cs" => "C# Code",
            ".js" or ".jsx" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript",
            ".py" => "Python",
            ".java" => "Java",
            ".go" => "Go",
            ".rb" => "Ruby",
            ".php" => "PHP",
            ".html" => "HTML",
            ".css" or ".scss" or ".sass" => "Styles",
            ".json" => "JSON",
            ".yaml" or ".yml" => "YAML",
            ".xml" => "XML",
            ".sql" => "SQL",
            ".sh" or ".bash" => "Shell Scripts",
            ".md" => "Markdown",
            ".dockerfile" or ".dockerignore" => "Docker",
            ".gitignore" or ".gitattributes" => "Git Config",
            _ => "Other Files"
        };
    }

    private bool IsConfigFile(string fileName, string extension)
    {
        var configFiles = new[]
        {
            "appsettings.json", "web.config", "app.config",
            "package.json", "package-lock.json", "yarn.lock",
            "requirements.txt", "pipfile", "poetry.lock",
            ".env", ".env.example", ".env.local",
            "tsconfig.json", "jsconfig.json",
            "webpack.config.js", "vite.config.js",
            "dockerfile", ".dockerignore",
            "docker-compose.yml", "docker-compose.yaml"
        };

        var configExtensions = new[] { ".config", ".ini", ".toml", ".properties" };

        return configFiles.Contains(fileName) ||
               configExtensions.Contains(extension);
    }

    private bool IsDocumentationFile(string fileName, string extension)
    {
        var docFiles = new[]
        {
            "readme.md", "contributing.md", "changelog.md",
            "license", "license.md", "license.txt"
        };

        var docExtensions = new[] { ".md", ".txt", ".rst", ".adoc" };

        return docFiles.Contains(fileName) ||
               docExtensions.Contains(extension);
    }

    private bool IsTestFile(string filePath, string fileName)
    {
        var testIndicators = new[]
        {
            "test", "tests", "spec", "specs",
            ".test.", ".spec.", "_test.", "_spec."
        };

        var lowerPath = filePath.ToLower();
        var lowerName = fileName.ToLower();

        return testIndicators.Any(indicator =>
            lowerPath.Contains(indicator) || lowerName.Contains(indicator));
    }

    private string GenerateFallbackSummary(PRContext context)
    {
        var summary = new List<string>();

        summary.Add($"Pull Request: {context.PrTitle}");

        if (!string.IsNullOrEmpty(context.PrDescription))
        {
            summary.Add($"Description: {context.PrDescription}");
        }

        if (context.ChangedFiles.Any())
        {
            summary.Add($"\nChanged Files: {context.ChangedFiles.Count}");

            var totalAdded = context.ChangedFiles.Sum(f => f.Added);
            var totalRemoved = context.ChangedFiles.Sum(f => f.Removed);

            summary.Add($"Lines Added: {totalAdded}");
            summary.Add($"Lines Removed: {totalRemoved}");

            summary.Add("\nTop Changed Files:");
            var topFiles = context.ChangedFiles
                .OrderByDescending(f => f.Added + f.Removed)
                .Take(5);

            foreach (var file in topFiles)
            {
                summary.Add($"  - {file.Path} (+{file.Added} -{file.Removed})");
            }
        }

        return string.Join("\n", summary);
    }

    public async Task<Dictionary<string, object>> GetPrStatisticsAsync(PRContext context)
    {
        var stats = new Dictionary<string, object>();

        try
        {
            stats["totalFiles"] = context.ChangedFiles.Count;
            stats["linesAdded"] = context.ChangedFiles.Sum(f => f.Added);
            stats["linesRemoved"] = context.ChangedFiles.Sum(f => f.Removed);
            stats["netChange"] = (int)stats["linesAdded"] - (int)stats["linesRemoved"];

            // File type breakdown
            var fileTypes = context.ChangedFiles
                .GroupBy(f => GetFileCategory(f.Path))
                .ToDictionary(
                    g => g.Key,
                    g => g.Count()
                );
            stats["fileTypes"] = fileTypes;

            // Status breakdown
            var statuses = context.ChangedFiles
                .GroupBy(f => f.Status)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count()
                );
            stats["statuses"] = statuses;

            // Complexity estimation
            var totalLines = (int)stats["linesAdded"] + (int)stats["linesRemoved"];
            stats["complexity"] = totalLines switch
            {
                < 100 => "Small",
                < 500 => "Medium",
                < 1000 => "Large",
                _ => "Very Large"
            };

            // Estimated review time (rough estimate)
            var reviewMinutes = Math.Max(5, totalLines / 20);
            stats["estimatedReviewTime"] = $"{reviewMinutes} minutes";

            return await Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate PR statistics");
            return stats;
        }
    }
}