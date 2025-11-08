using GitHubPRAssistant.Models;
using Octokit;
using Polly;
using System.Net;
using System.Text.Json;


namespace GitHubPRAssistant.Services
{
    public class GitHubService
    {
        private readonly IGitHubClient _client;
        private readonly ILogger<GitHubService> _logger;
        private readonly string _token;
        private readonly IAsyncPolicy<IReadOnlyList<PullRequestFile>> _prFilesPolicy;
        private readonly IAsyncPolicy<IReadOnlyList<RepositoryContent>> _contentPolicy;
        private readonly IAsyncPolicy<IssueComment> _commentPolicy;

        public GitHubService(IConfiguration config, ILogger<GitHubService> logger, IGitHubClient client)
        {
            _logger = logger;
            _token = config["GitHub:Token"] ?? throw new ArgumentNullException("GitHubToken is not configured");
            _client = client;

            // Configure retry policies with exponential backoff
            var retryPolicy = Policy
                .Handle<RateLimitExceededException>()
                .Or<ApiException>(ex => ex.StatusCode == (HttpStatusCode)429)
                .Or<NotFoundException>()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (ex, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(ex,
                    "Retry {RetryCount} after {RetryTimeSpan}s delay due to {ExceptionType}",
                retryCount, timeSpan.TotalSeconds, ex.GetType().Name);
                    });

            _prFilesPolicy = retryPolicy.AsAsyncPolicy<IReadOnlyList<PullRequestFile>>();
            _contentPolicy = retryPolicy.AsAsyncPolicy<IReadOnlyList<RepositoryContent>>();
            _commentPolicy = retryPolicy.AsAsyncPolicy<IssueComment>();
        }

        public async Task<PRContext> ParsePrWebHookAsync(JsonElement payload)
        {
            try
            {
                var prElement = payload.GetProperty("pull_request");
                var repoElement = prElement.GetProperty("head").GetProperty("repo");
                var owner = repoElement.GetProperty("owner").GetProperty("login").GetString() ?? string.Empty;
                var repo = repoElement.GetProperty("name").GetString() ?? string.Empty;
                var prNumber = prElement.GetProperty("number").GetInt32();
                var title = prElement.GetProperty("title").GetString() ?? string.Empty;
                var description = prElement.GetProperty("body").GetString() ?? string.Empty;

                var context = new PRContext
                {
                    Owner = owner,
                    Repo = repo,
                    PrNumber = prNumber,
                    PrTitle = title,
                    PrDescription = description,
                };


                // Fetch changed files with retry policy
                context.ChangedFiles = await GetPrFilesAsync(context);

                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing PR webhook payload");
                throw;
            }   
        }

        public async Task<List<ChangedFile>> GetPrFilesAsync(PRContext context)
        {
            try
            {
                var files = await _prFilesPolicy.ExecuteAsync(async () => 
         await _client.PullRequest.Files(context.Owner, context.Repo, context.PrNumber));
           
                var changedFiles = new List<ChangedFile>();
                var semaphore = new SemaphoreSlim(5); // Limit concurrent requests

                var tasks = files.Select(async file =>
                {
                    string content = "";
                    if (file.Additions + file.Deletions < 1000 && IsCodeFile(file.FileName))
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            content = await GetFileContentWithRetryAsync(context, file);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }

                    return new ChangedFile
                    {
                        Path = file.FileName,
                        Content = content,
                        Added = file.Additions,
                        Removed = file.Deletions,
                        Status = file.Status
                    };
                });

                changedFiles.AddRange(await Task.WhenAll(tasks));
                return changedFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch PR files for {Owner}/{Repo}#{PrNumber}", 
       context.Owner, context.Repo, context.PrNumber);
                throw;
            }
        }

        private async Task<string> GetFileContentWithRetryAsync(PRContext context, PullRequestFile file)
        {
            try
            {
                var fileContent = await _contentPolicy.ExecuteAsync(async () =>
                    await _client.Repository.Content.GetAllContentsByRef(
   context.Owner,
       context.Repo,
   file.FileName,
       file.Sha
     ));

                if (fileContent.Count > 0 && fileContent[0].Type == ContentType.File)
                {
                    return fileContent[0].Content ?? "";
                }
            }
            catch (NotFoundException)
            {
                _logger.LogInformation("File not found in repository: {FileName}", file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch content for {FileName}", file.FileName);
            }

            return "";
        }

        public async Task PostPrCommentAsync(PRContext context, string comment)
        {
            try
            {
                await _commentPolicy.ExecuteAsync(async () =>
    await _client.Issue.Comment.Create(
         context.Owner,
    context.Repo,
             context.PrNumber,
     comment
 ));

                _logger.LogInformation("Posted review comment to PR #{Number}", context.PrNumber);
 }
            catch (Exception ex)
      {
    _logger.LogError(ex, "Failed to post PR comment to {Owner}/{Repo}#{PrNumber}", 
         context.Owner, context.Repo, context.PrNumber);
    throw;
            }
        }

        public async Task<List<string>> GetPrDiffsAsync(PRContext context)
        {
            try
            {
   var files = await _prFilesPolicy.ExecuteAsync(async () =>
        await _client.PullRequest.Files(context.Owner, context.Repo, context.PrNumber));
         
     return files.Select(f => $"{f.FileName}: +{f.Additions} -{f.Deletions}").ToList();
            }
            catch (Exception ex)
     {
                _logger.LogError(ex, "Failed to fetch PR diffs for {Owner}/{Repo}#{PrNumber}", 
  context.Owner, context.Repo, context.PrNumber);
         throw;
    }
}

        private static bool IsCodeFile(string fileName)
        {
    var codeExtensions = new[] { ".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".java",
         ".go", ".rb", ".php", ".cpp", ".c", ".h", ".swift",
       ".kt", ".rs", ".sql", ".json", ".yaml", ".yml" };
    return codeExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }
    }
}
