using GitHubPRAssistant.Models;
using Octokit;
using System.Text.Json;


namespace GitHubPRAssistant.Services
{
    public class GitHubService
    {
        private readonly GitHubClient _client;
        private readonly ILogger<GitHubService> _logger;
        private readonly string _token;

        public GitHubService(IConfiguration config, ILogger<GitHubService> logger)
        {
            _logger = logger;
            _token = config["GitHub:Token"] ?? throw new ArgumentNullException("GitHubToken is not configured");
            _client = new GitHubClient(new ProductHeaderValue("GitHubPRAssistant"))
            {
                Credentials = new Credentials(_token)
            };
        }
        public async Task<PRContext> ParsePrWebHookAsync(JsonElement payload)
        {
            try
            {
                var prElement = payload.GetProperty("pull_request");
                Console.WriteLine(prElement.ToString());
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


                // FEtch changed files
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
                var files = await _client.PullRequest.Files(context.Owner, context.Repo, context.PrNumber);
                var changedFiles = new List<ChangedFile>();

                foreach (var file in files)
                {
                    // Only fetch content for code files under 100KB
                    string content = "";
                    if (file.Additions + file.Deletions < 1000 && IsCodeFile(file.FileName))
                    {
                        try
                        {
                            var fileContent = await _client.Repository.Content.GetAllContentsByRef(
                                context.Owner,
                                context.Repo,
                                file.FileName,
                                file.Sha
                            );

                            if (fileContent.Count > 0 && fileContent[0].Type == ContentType.File)
                            {
                                content = fileContent[0].Content ?? "";
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not fetch content for {FileName}", file.FileName);
                        }
                    }

                    changedFiles.Add(new ChangedFile
                    {
                        Path = file.FileName,
                        Content = content,
                        Added = file.Additions,
                        Removed = file.Deletions,
                        Status = file.Status
                    });
                }

                return changedFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch PR files");
                throw;
            }
        }

        public async Task PostPrCommentAsync(PRContext context, string comment)
        {
            try
            {
                await _client.Issue.Comment.Create(
                    context.Owner,
                    context.Repo,
                    context.PrNumber,
                    comment
                );

                _logger.LogInformation("Posted review comment to PR #{Number}", context.PrNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post PR comment");
                throw;
            }
        }

        public async Task<List<string>> GetPrDiffsAsync(PRContext context)
        {
            try
            {
                var files = await _client.PullRequest.Files(context.Owner, context.Repo, context.PrNumber);
                return files.Select(f => $"{f.FileName}: +{f.Additions} -{f.Deletions}").ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch PR diffs");
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
