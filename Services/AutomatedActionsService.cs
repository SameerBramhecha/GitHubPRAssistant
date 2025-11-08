using GitHubPRAssistant.Models;
using LibGit2Sharp;
using Octokit;
using Polly;
using System.Net;

namespace GitHubPRAssistant.Services
{
    public class AutomatedActionsService
    {
        private readonly IGitHubClient _github;
        private readonly ILogger<AutomatedActionsService> _logger;
        private readonly IConfiguration _config;
        private readonly IAsyncPolicy<CheckRunsResponse> _checkRunPolicy;
        private readonly IAsyncPolicy<PullRequest> _pullRequestPolicy;
        private readonly IAsyncPolicy<IReadOnlyList<RepositoryContent>> _repoContentPolicy;
        private readonly IAsyncPolicy _gitOperationPolicy;

        public AutomatedActionsService(
            IGitHubClient gitHubClient, 
            ILogger<AutomatedActionsService> logger, 
            IConfiguration config)
        {
            _github = gitHubClient;
            _logger = logger;
            _config = config;

            // Configure retry policies with exponential backoff
            var retryPolicy = Policy
                .Handle<RateLimitExceededException>()
                .Or<ApiException>(ex => ex.StatusCode == (HttpStatusCode)429)
                .Or<Octokit.NotFoundException>()
                .Or<ForbiddenException>()
                .WaitAndRetryAsync(3, retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (ex, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(ex, 
                        "Retry {RetryCount} after {RetryTimeSpan}s delay due to {ExceptionType}", 
                        retryCount, timeSpan.TotalSeconds, ex.GetType().Name);
                });

            _checkRunPolicy = retryPolicy.AsAsyncPolicy<CheckRunsResponse>();
            _pullRequestPolicy = retryPolicy.AsAsyncPolicy<PullRequest>();
            _repoContentPolicy = retryPolicy.AsAsyncPolicy<IReadOnlyList<RepositoryContent>>();
            _gitOperationPolicy = retryPolicy;
        }

        // Evaluate if PR should be auto-approved
        public async Task<(bool ShouldApprove, string Reason)> EvaluateAutoApprovalAsync(
            PRContext context,
            AgentResult reviewResult)
        {
            var criteria = new List<(bool Passed, string Criterion)>();

            try
            {
                // Criterion 1: No critical issues
                criteria.Add((
                    !reviewResult.HasCriticalIssues,
                    "No critical security or quality issues"
                ));

                // Criterion 2: Small change size
                var totalChanges = context.ChangedFiles.Sum(f => f.Added + f.Removed);
                criteria.Add((
                    totalChanges < 100,
                    $"Small change size ({totalChanges} lines)"
                ));

                // Criterion 3: Only documentation or config changes
                var onlyDocsOrConfig = context.ChangedFiles.All(f =>
                    f.Path.EndsWith(".md") ||
                    f.Path.EndsWith(".json") ||
                    f.Path.EndsWith(".yml") ||
                    f.Path.EndsWith(".yaml") ||
                    f.Path.Contains("docs/")
                );
                criteria.Add((
                    onlyDocsOrConfig,
                    "Only documentation or configuration changes"
                ));

                // Criterion 4: Tests are included (if code changes)
                var hasCodeChanges = context.ChangedFiles.Any(f =>
                    f.Path.EndsWith(".cs") || f.Path.EndsWith(".js") || f.Path.EndsWith(".ts")
                );
                var hasTestChanges = context.ChangedFiles.Any(f =>
                    f.Path.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                    f.Path.Contains("spec", StringComparison.OrdinalIgnoreCase)
                );
                criteria.Add((
                    !hasCodeChanges || hasTestChanges,
                    "Tests included for code changes"
                ));

                // Criterion 5: No failed checks with enhanced retry logic and error handling
                try
                {
                    var checksResponse = await _checkRunPolicy.ExecuteAsync(async () => 
                        await _github.Check.Run.GetAllForReference(
                            context.Owner,
                            context.Repo,
                            $"refs/pull/{context.PrNumber}/head"
                        ));

                    var allChecksPassed = checksResponse.CheckRuns.All(c =>
                        c.Status == CheckStatus.Completed && c.Conclusion == CheckConclusion.Success
                    );
                    criteria.Add((
                        allChecksPassed,
                        "All CI/CD checks passed"
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to verify CI/CD checks. Marking criterion as failed.");
                    criteria.Add((false, "Unable to verify CI/CD checks"));
                }

                // Criterion 6: From trusted author with enhanced error handling
                try
                {
                    var trustedAuthors = _config.GetSection("AutoApproval:TrustedAuthors").Get<List<string>>() ?? new();
                    var pr = await _pullRequestPolicy.ExecuteAsync(async () =>
                        await _github.PullRequest.Get(context.Owner, context.Repo, context.PrNumber));
                    
                    var isTrustedAuthor = trustedAuthors.Contains(pr.User.Login);
                    criteria.Add((
                        isTrustedAuthor || trustedAuthors.Count == 0,
                        $"Author is trusted: {pr.User.Login}"
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to verify author trust status. Marking criterion as failed.");
                    criteria.Add((false, "Unable to verify author trust status"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating auto-approval criteria");
                // If there's an error evaluating criteria, return false to be safe
                return (false, "Error evaluating approval criteria: " + ex.Message);
            }

            // All criteria must pass for auto-approval
            var allPassed = criteria.All(c => c.Passed);
            var reason = string.Join("\n", criteria.Select(c =>
                $"{(c.Passed ? "✅" : "❌")} {c.Criterion}"
            ));

            return (allPassed, reason);
        }

        // Automatically approve PR
        public async Task ApprovePrAsync(PRContext context, string reason)
        {
            try
            {
                await _github.PullRequest.Review.Create(
                    context.Owner,
                    context.Repo,
                    context.PrNumber,
                    new PullRequestReviewCreate
                    {
                        Event = PullRequestReviewEvent.Approve,
                        Body = $@"✅ **Auto-Approved by AI PR Assistant**

{reason}

This PR meets all auto-approval criteria and has been automatically approved.

---
*🤖 Automated approval - Review criteria passed*"
                    }
                );

                _logger.LogInformation("Auto-approved PR #{Number}", context.PrNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-approve PR #{Number}", context.PrNumber);
                throw;
            }
        }

        // Request changes for critical issues
        public async Task RequestChangesAsync(PRContext context, List<string> criticalIssues)
        {
            try
            {
                var issuesList = string.Join("\n", criticalIssues.Select((i, idx) => $"{idx + 1}. {i}"));

                await _github.PullRequest.Review.Create(
                    context.Owner,
                    context.Repo,
                    context.PrNumber,
                    new PullRequestReviewCreate
                    {
                        Event = PullRequestReviewEvent.RequestChanges,
                        Body = $@"❌ **Changes Requested by AI PR Assistant**

Critical issues were found that must be addressed before merging:

{issuesList}

Please fix these issues and push new commits.

---
*🤖 Automated review - Critical issues detected*"
                    }
                );

                _logger.LogInformation("Requested changes on PR #{Number}", context.PrNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request changes on PR #{Number}", context.PrNumber);
                throw;
            }
        }

        // Auto-fix minor issues with enhanced error handling
        public async Task<bool> AutoFixIssuesAsync(PRContext context, List<string> fixableIssues)
        {
            if (!fixableIssues.Any())
                return false;

            try
            {
                _logger.LogInformation("Attempting to auto-fix {Count} issues", fixableIssues.Count);

                // Clone with retry
                var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                var pr = await _pullRequestPolicy.ExecuteAsync(async () =>
                    await _github.PullRequest.Get(context.Owner, context.Repo, context.PrNumber));

                var cloneOptions = new CloneOptions
                {
                    CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                    {
                        Username = _config["GitHub:Token"],
                        Password = string.Empty
                    }
                };

                LibGit2Sharp.Repository repo = null;
                await _gitOperationPolicy.ExecuteAsync(async () =>
                {
                    repo = new LibGit2Sharp.Repository(LibGit2Sharp.Repository.Clone(
                        $"https://github.com/{context.Owner}/{context.Repo}.git",
                        tempPath,
                        cloneOptions
                    ));
                    return Task.CompletedTask;
                });

                using (repo)
                {
                    try
                    {
                        // Checkout with retry
                        await _gitOperationPolicy.ExecuteAsync(async () =>
                        {
                            var branch = repo.Branches[pr.Head.Ref];
                            Commands.Checkout(repo, branch);
                            return Task.CompletedTask;
                        });

                        var fixedCount = 0;
                        foreach (var issue in fixableIssues)
                        {
                            if (await TryFixIssueAsync(repo, tempPath, issue))
                            {
                                fixedCount++;
                            }
                        }

                        if (fixedCount > 0)
                        {
                            // Stage and commit with retry
                            await _gitOperationPolicy.ExecuteAsync(async () =>
                            {
                                Commands.Stage(repo, "*");
                                var signature = new LibGit2Sharp.Signature("AI PR Assistant", "bot@example.com", DateTimeOffset.Now);
                                repo.Commit(
                                    $"🤖 Auto-fix: Fixed {fixedCount} minor issues\n\n" +
                                    string.Join("\n", fixableIssues.Take(fixedCount)),
                                    signature,
                                    signature
                                );

                                var pushOptions = new PushOptions
                                {
                                    CredentialsProvider = cloneOptions.CredentialsProvider
                                };
                                repo.Network.Push(repo.Head, pushOptions);
                                return Task.CompletedTask;
                            });

                            _logger.LogInformation("Auto-fixed {Count} issues on PR #{Number}", fixedCount, context.PrNumber);

                            // Post comment with retry
                            await _gitOperationPolicy.ExecuteAsync(async () =>
                            {
                                await _github.Issue.Comment.Create(
                                    context.Owner,
                                    context.Repo,
                                    context.PrNumber,
                                    $@"🤖 **Auto-Fix Applied**

I've automatically fixed {fixedCount} minor issues:

{string.Join("\n", fixableIssues.Take(fixedCount).Select((i, idx) => $"{idx + 1}. {i}"))}

Changes have been pushed to this PR."
                                );
                            });

                            return true;
                        }
                    }
                    finally
                    {
                        try
                        {
                            // Cleanup
                            if (Directory.Exists(tempPath))
                            {
                                Directory.Delete(tempPath, recursive: true);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to cleanup temporary directory: {TempPath}", tempPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-fix issues");
                return false;
            }

            return false;
        }

        private async Task<bool> TryFixIssueAsync(LibGit2Sharp.Repository repo, string repoPath, string issue)
        {
            try
            {
                // Parse issue to determine fix type
                if (issue.Contains("console.log"))
                {
                    return await RemoveConsoleLogsAsync(repoPath);
                }
                else if (issue.Contains("TODO"))
                {
                    // Don't auto-fix TODOs
                    return false;
                }
                else if (issue.Contains("Potential secret"))
                {
                    // Don't auto-fix secrets (too dangerous)
                    return false;
                }
                else if (issue.Contains("formatting") || issue.Contains("whitespace"))
                {
                    return await FormatCodeAsync(repoPath);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fix issue: {Issue}", issue);
                return false;
            }
        }

        private async Task<bool> RemoveConsoleLogsAsync(string repoPath)
        {
            var jsFiles = Directory.GetFiles(repoPath, "*.js", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(repoPath, "*.ts", SearchOption.AllDirectories));

            var modified = false;

            foreach (var file in jsFiles)
            {
                var content = await File.ReadAllTextAsync(file);
                var newContent = System.Text.RegularExpressions.Regex.Replace(
                    content,
                    @"^\s*console\.(log|debug|info)\([^)]*\);\s*$",
                    "",
                    System.Text.RegularExpressions.RegexOptions.Multiline
                );

                if (content != newContent)
                {
                    await File.WriteAllTextAsync(file, newContent);
                    modified = true;
                }
            }

            return modified;
        }

        private async Task<bool> FormatCodeAsync(string repoPath)
        {
            // Run code formatters
            try
            {
                // For C# files
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"format \"{repoPath}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
