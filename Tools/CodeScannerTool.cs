using GitHubPRAssistant.Models;
using System.Text.RegularExpressions;

namespace GitHubPRAssistant.Tools
{
    public class CodeScannerTool
    {
        private readonly ILogger<CodeScannerTool> _logger;

        // Security patterns
        private static readonly Regex SecretPattern = new(
            @"(API[_-]?KEY|SECRET|PASSWORD|TOKEN|CREDENTIALS?|AUTH[_-]?KEY)\s*[:=]\s*[""'][\w\-]{8,}[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex HardcodedIpPattern = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            RegexOptions.Compiled
        );

        private static readonly Regex TodoPattern = new(
            @"\b(TODO|FIXME|HACK|XXX|BUG)\b:?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex ConsoleLogPattern = new(
            @"console\.(log|debug|info|warn|error)\(",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex DebuggerPattern = new(
            @"\bdebugger\s*;",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Code quality patterns
        private static readonly Regex LongLinePattern = new(
            @"^.{150,}$",
            RegexOptions.Compiled | RegexOptions.Multiline
        );

        public CodeScannerTool(ILogger<CodeScannerTool> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> ScanAsync(PRContext context)
        {
            var issues = new List<string>();

            foreach (var file in context.ChangedFiles.Where(f => !string.IsNullOrEmpty(f.Content)))
            {
                try
                {
                    // Security checks
                    CheckForSecrets(file, issues);
                    CheckForHardcodedIps(file, issues);

                    // Code quality checks
                    CheckForTodos(file, issues);
                    CheckForDebugStatements(file, issues);
                    CheckFileSize(file, issues);
                    CheckForLongLines(file, issues);

                    // Language-specific checks
                    if (file.Path.EndsWith(".cs"))
                    {
                        CheckCSharpPatterns(file, issues);
                    }
                    else if (file.Path.EndsWith(".js") || file.Path.EndsWith(".ts"))
                    {
                        CheckJavaScriptPatterns(file, issues);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning file {Path}", file.Path);
                }
            }

            _logger.LogInformation("Code scan completed: {IssueCount} issues found", issues.Count);
            return await Task.FromResult(issues);
        }

        private void CheckForSecrets(ChangedFile file, List<string> issues)
        {
            if (SecretPattern.IsMatch(file.Content))
            {
                issues.Add($"⚠️ **Potential secret detected** in `{file.Path}` - Please verify no sensitive data is committed");
            }
        }

        private void CheckForHardcodedIps(ChangedFile file, List<string> issues)
        {
            var matches = HardcodedIpPattern.Matches(file.Content);
            if (matches.Count > 0)
            {
                var ips = matches.Select(m => m.Value).Distinct().Take(3);
                issues.Add($"⚠️ **Hardcoded IP address(es)** in `{file.Path}`: {string.Join(", ", ips)} - Consider using configuration");
            }
        }

        private void CheckForTodos(ChangedFile file, List<string> issues)
        {
            var matches = TodoPattern.Matches(file.Content);
            if (matches.Count > 0)
            {
                issues.Add($"📝 **{matches.Count} TODO/FIXME** comment(s) in `{file.Path}` - Consider addressing before merge");
            }
        }

        private void CheckForDebugStatements(ChangedFile file, List<string> issues)
        {
            var consoleMatches = ConsoleLogPattern.Matches(file.Content);
            var debuggerMatches = DebuggerPattern.Matches(file.Content);

            if (consoleMatches.Count > 2)
            {
                issues.Add($"🐛 **{consoleMatches.Count} console.log statements** in `{file.Path}` - Consider removing debug logs");
            }

            if (debuggerMatches.Count > 0)
            {
                issues.Add($"🐛 **Debugger statement** found in `{file.Path}` - Should be removed before merge");
            }
        }

        private void CheckFileSize(ChangedFile file, List<string> issues)
        {
            const int MaxChars = 200_000;
            const int LargeFileChars = 50_000;

            if (file.Content.Length > MaxChars)
            {
                issues.Add($"📦 **Very large file** `{file.Path}` ({file.Content.Length / 1000}KB) - Consider breaking into smaller files");
            }
            else if (file.Content.Length > LargeFileChars && file.Added > file.Removed)
            {
                issues.Add($"📦 **Large file addition** in `{file.Path}` ({file.Content.Length / 1000}KB) - Verify this is intentional");
            }
        }

        private void CheckForLongLines(ChangedFile file, List<string> issues)
        {
            var longLines = LongLinePattern.Matches(file.Content);
            if (longLines.Count > 5)
            {
                issues.Add($"📏 **{longLines.Count} lines over 150 characters** in `{file.Path}` - Consider breaking for readability");
            }
        }

        private void CheckCSharpPatterns(ChangedFile file, List<string> issues)
        {
            // Check for empty catch blocks
            if (Regex.IsMatch(file.Content, @"catch\s*\([^)]*\)\s*\{\s*\}"))
            {
                issues.Add($"⚠️ **Empty catch block** in `{file.Path}` - Should handle or log exceptions");
            }

            // Check for synchronous file operations
            if (Regex.IsMatch(file.Content, @"File\.(ReadAllText|WriteAllText|ReadAllBytes|WriteAllBytes)\(") &&
                !file.Content.Contains("Async"))
            {
                issues.Add($"⚡ **Synchronous file operations** in `{file.Path}` - Consider using async versions");
            }
        }

        private void CheckJavaScriptPatterns(ChangedFile file, List<string> issues)
        {
            // Check for == instead of ===
            var equalityMatches = Regex.Matches(file.Content, @"[^!=]==[^=]");
            if (equalityMatches.Count > 2)
            {
                issues.Add($"⚠️ **Loose equality (==)** used in `{file.Path}` - Consider using strict equality (===)");
            }

            // Check for var usage
            if (Regex.IsMatch(file.Content, @"\bvar\s+\w+\s*="))
            {
                issues.Add($"💡 **'var' keyword** used in `{file.Path}` - Consider using 'const' or 'let'");
            }
        }
    }
}
