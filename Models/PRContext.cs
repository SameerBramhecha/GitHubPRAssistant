namespace GitHubPRAssistant.Models
{
    public class PRContext
    {
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        public int PrNumber { get; set; }
        public string PrTitle { get; set; } = string.Empty;
        public string PrDescription { get; set; } = string.Empty;
        public List<ChangedFile> ChangedFiles { get; set; } = new();

    }
}
