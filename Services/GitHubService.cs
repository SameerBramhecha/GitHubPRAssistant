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
        private readonly IAsyncPolicy<PullRequest> _pullRequestPolicy;
        private readonly IAsyncPolicy<Repository> _repositoryPolicy;

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
            _pullRequestPolicy = retryPolicy.AsAsyncPolicy<PullRequest>();
            _repositoryPolicy = retryPolicy.AsAsyncPolicy<Repository>();
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
      // Preferred fallback order: (1) PR head ref, (2) Git blob API, (3) raw.githubusercontent

      // Fetch PR to get head ref
      PullRequest pr = null;
      try
      {
         pr = await _pullRequestPolicy.ExecuteAsync(async () =>
   await _client.PullRequest.Get(context.Owner, context.Repo, context.PrNumber));
      }
      catch (Exception ex)
      {
         _logger.LogDebug(ex, "Could not fetch PR object; will continue with context owner/repo");
      }

      // Determine head ref and head repo owner/name to use (default to context values)
      var headOwner = context.Owner;
      var headRepo = context.Repo;
      var headRef = (string?)null;

      if (pr != null)
      {
         headRef = pr.Head?.Sha ?? pr.Head?.Ref;
         // Note: context.Owner/context.Repo should already reflect the PR head repo from webhook parsing
      }

      //1) Try PR head ref if available
      if (!string.IsNullOrEmpty(headRef))
      {
         try
         {
            _logger.LogDebug("Trying file content from PR head ref: {Owner}/{Repo}@{Ref} path={Path}", headOwner, headRepo, headRef, file.FileName);
            var fileContent = await _contentPolicy.ExecuteAsync(async () =>
   await _client.Repository.Content.GetAllContentsByRef(headOwner, headRepo, file.FileName, headRef));

            if (fileContent.Count >0 && fileContent[0].Type == ContentType.File)
            {
               return fileContent[0].Content ?? string.Empty;
            }
         }
         catch (NotFoundException nf)
         {
            _logger.LogDebug(nf, "Not found at PR head ref: {Owner}/{Repo}@{Ref} path={Path}", headOwner, headRepo, headRef, file.FileName);
         }
         catch (Exception ex)
         {
            _logger.LogWarning(ex, "Error fetching content from PR head ref for {Path}", file.FileName);
         }
      }

      //2) If file.Sha looks like a blob SHA (40 hex) try Git blob API
      if (!string.IsNullOrEmpty(file.Sha) && file.Sha.Length ==40 && System.Text.RegularExpressions.Regex.IsMatch(file.Sha, "^[0-9a-fA-F]{40}$"))
      {
         try
         {
            _logger.LogDebug("Trying Git blob API for {Owner}/{Repo} blob={Blob} path={Path}", headOwner, headRepo, file.Sha, file.FileName);
            var blob = await _client.Git.Blob.Get(headOwner, headRepo, file.Sha);
            if (!string.IsNullOrEmpty(blob.Content))
            {
               // Encoding is a StringEnum<EncodingType>; compare its Value to EncodingType.Base64
               if (blob.Encoding != null && blob.Encoding.Value == EncodingType.Base64)
               {
                  var bytes = Convert.FromBase64String(blob.Content);
                  return System.Text.Encoding.UTF8.GetString(bytes);
               }

               return blob.Content;
            }
         }
         catch (NotFoundException nf)
         {
            _logger.LogDebug(nf, "Blob not found: {Blob}", file.Sha);
         }
         catch (Exception ex)
         {
            _logger.LogWarning(ex, "Error fetching blob content for {Blob}", file.Sha);
         }
      }

      //3) Try raw.githubusercontent using PR head sha if available
      if (!string.IsNullOrEmpty(headRef))
      {
         try
         {
            var rawUrl = $"https://raw.githubusercontent.com/{headOwner}/{headRepo}/{headRef}/{file.FileName}";
            _logger.LogDebug("Trying raw URL: {RawUrl}", rawUrl);

            var response = await _client.Connection.Get<string>(new Uri(rawUrl), new Dictionary<string, string>(), "application/vnd.github.v3.raw");
            if (response.Body != null)
            {
               return response.Body;
            }
         }
         catch (Exception ex)
         {
            _logger.LogDebug(ex, "Failed to fetch raw content from raw.githubusercontent");
         }
      }

      //4) Fallback: try default branch of repository
      try
      {
         var repoObj = await _repositoryPolicy.ExecuteAsync(async () => await _client.Repository.Get(context.Owner, context.Repo));
         if (repoObj != null && !string.IsNullOrEmpty(repoObj.DefaultBranch))
         {
            try
            {
               _logger.LogDebug("Trying default branch {Branch} for {Owner}/{Repo} path={Path}", repoObj.DefaultBranch, context.Owner, context.Repo, file.FileName);
               var fileContent = await _contentPolicy.ExecuteAsync(async () =>
   await _client.Repository.Content.GetAllContentsByRef(context.Owner, context.Repo, file.FileName, repoObj.DefaultBranch));

               if (fileContent.Count >0 && fileContent[0].Type == ContentType.File)
               {
                  return fileContent[0].Content ?? string.Empty;
               }
            }
            catch (Exception ex)
            {
               _logger.LogDebug(ex, "Failed to fetch content from default branch");
            }
         }
      }
      catch (Exception ex)
      {
         _logger.LogDebug(ex, "Failed to fetch repository to check default branch");
      }
   }
            catch (Exception ex)
  {
       _logger.LogWarning(ex, "All attempts to fetch content failed for {FileName}", file.FileName);
 }

            _logger.LogInformation("File not found in repository after all attempts: {FileName}", file.FileName);
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

        private async Task<bool> FileExistsInRef(string owner, string repo, string path, string reference)
     {
     try
            {
                await _contentPolicy.ExecuteAsync(async () =>
           await _client.Repository.Content.GetAllContentsByRef(owner, repo, path, reference));
                return true;
            }
            catch (NotFoundException)
            {
          return false;
       }
      catch (Exception)
  {
       return false;
   }
        }

      // ... rest of the class implementation ...
    }
}
