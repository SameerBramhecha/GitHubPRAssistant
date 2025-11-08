namespace GitHubPRAssistant.Models
{
    public class ChangedFile
    {
        public string Path { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int Added { get; set; }
        public int Removed { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
