namespace GitHubPRAssistant.Models
{
    public class AgentResult
    {
        public string Comment { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> Issues { get; set; } = new();
        public bool HasCriticalIssues { get; set; }
    }
}
