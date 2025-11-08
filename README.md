# GitHub PR Assistant

🤖 AI GitHub Pull Request Assistant
An intelligent ASP.NET Core agent service that automatically reviews GitHub Pull Requests using LLM-powered analysis and deterministic code scanning.
✨ Features

🔍 Automated PR Analysis - Summarizes changes and identifies patterns
🛡️ Security Scanning - Detects hardcoded secrets, credentials, and IPs
📊 Code Quality Checks - Finds TODOs, debug statements, and code smells
🎯 Smart Reviews - Generates actionable, constructive feedback
🔗 GitHub Integration - Posts comments directly to PRs

## 📋 Prerequisites

- .NET 9 SDK
- GitHub Account and Personal Access Token
- OpenAI API Key
- Visual Studio 2022 or compatible IDE

## ⚙️ Configuration

Create or update your `appsettings.json` with the following configurations:
```json
{ 
	"Logging": { 
		"LogLevel": { 
			"Default": "Information", 
			"Microsoft.AspNetCore": "Warning", 
			"GitHubPRAssistant": "Debug" 
		} 
	}, 
	"AllowedHosts": "*",
	"GitHub": { 
		"Token": "your-github-pat", 
		"WebhookSecret": "your-webhook-secret" 
	}, 
	"OpenAI": { 
	"ApiKey": "your-openai-api-key", 
	"Model": "gpt-4o-mini" 
	} 
}
```


## 🛠️ Project Structure

### Core Services

1. **LlmService (`Services/LlmService.cs`)**
   - Handles communication with OpenAI's API
   - Manages chat completions and response generation
   - Supports customizable temperature and token limits
   - Features:
     - Generic text generation
     - PR summarization
     - Review comment generation
   - Uses Microsoft AI Agents Framework for enhanced capabilities

2. **GitHubService**
   - Manages GitHub API interactions
   - Handles webhook events
   - Retrieves PR information and diffs
   - Integrates with Octokit for GitHub API communication

### Tools

1. **PrSummarizerTool (`Tools/PRSummarizerTool.cs`)**
   - Generates comprehensive PR summaries
   - Features:
     - File categorization
     - Change statistics
     - Complexity analysis
     - Review time estimation
   - Supports multiple file types:
     - Configuration files
     - Documentation
     - Test files
     - Various programming languages

2. **CodeScannerTool (`Tools/CodeScannerTool.cs`)**
   - Performs static code analysis
   - Detects potential issues and code smells
   - Checks for:
     - Debugger statements
     - File size violations
     - Long lines
     - Empty catch blocks
     - Synchronous operations
     - JavaScript-specific patterns

### Program Structure


The application is built as an ASP.NET Core web application with the following components:
```csharp
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<CodeScannerTool>();
builder.Services.AddSingleton<LlmService>();
builder.Services.AddSingleton<PrSummarizerTool>();
builder.Services.AddSingleton<PrReviewAgent>();
```

### Agent Components

1. **PRReviewAgent**
   - Coordinates the review process
   - Manages the workflow between different tools
   - Handles the integration of LLM responses
   - Generates final review comments

### Agent Workflow

1. Context Collection
   - Gathers PR metadata
   - Collects changed files
   - Prepares analysis context

2. Analysis Phase
   - Runs code scanner
   - Processes file changes
   - Generates summaries

3. Review Generation
   - Creates comprehensive review comments
   - Formats feedback
   - Adds metadata and statistics


## 📊 Features Detail

### PR Analysis Capabilities

- **File Categorization**
- Configuration files (`.json`, `.config`, etc.)
- Documentation (`.md`, `.txt`, etc.)
- Test files
- Programming language files
- Docker and deployment files

- **Statistics Generation**
- Total files changed
- Lines added/removed
- File type breakdown
- Change complexity estimation
- Estimated review time

### LLM Integration

- **Summary Generation**
- Concise bullet points
- Focus on intent and impact
- Changed files analysis

- **Review Comments**
- Structured feedback
- Key findings
- Actionable recommendations
- Verification checklist


## 🚀 Setup Instructions

1. **Clone the Repository**

```bash
git clone <repository-url> cd github-pr-assistant
```

2. **Install Dependencies**

```bash
dotnet restore
```

3. **Configure Secrets**
   - Create a GitHub Personal Access Token
   - Obtain an OpenAI API key
   - Update `appsettings.json` or use User Secrets

4. **Build and Run**

```bash
dotnet build
dotnet run
```

6. **Set Up GitHub Webhook**
   - Configure a webhook in your GitHub repository settings
   - Point it to your running application URL
   - Use the configured webhook secret


## 🔒 Security Considerations

-Store sensitive credentials in secure configuration management
- Use environment variables for production deployments
- Regularly rotate API keys and tokens
- Validate webhook signatures
- Implement rate limiting for API calls

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request