Below is a **beautified, polished, and professional README.md** version of your content — with better formatting, icons, spacing, and clarity.

---

# 🤖 GitHub PR Assistant

An intelligent **AI-powered ASP.NET Core service** that automatically reviews GitHub Pull Requests using **LLM-driven analysis** and **deterministic code scanning**.
It acts like your AI teammate — analyzing code changes, detecting issues, and giving structured PR feedback.

---

## ✨ Key Features

| Capability                   | Description                                         |
| ---------------------------- | --------------------------------------------------- |
| 🔍 **Automated PR Analysis** | Understands code changes & developer intent         |
| 🛡️ **Security Scanning**    | Detects secrets, sensitive values, and unsafe code  |
| 🧹 **Code Quality Checks**   | Spots debug logs, TODOs, smells & anti-patterns     |
| 🧠 **LLM Smart Reviews**     | Generates helpful, actionable pull-request comments |
| 🔗 **GitHub Integration**    | Receives webhooks & comments directly on PRs        |

---

## 📦 Prerequisites

* ✅ .NET 9 SDK
* ✅ GitHub Account & Personal Access Token
* ✅ OpenAI API Key
* ✅ Visual Studio 2022 / VS Code

---

## ⚙️ Configuration

Add settings to `appsettings.json`:

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

---

## 🧠 Architecture Overview

### Core Services

| Component            | Responsibilities                                   |
| -------------------- | -------------------------------------------------- |
| **LlmService**       | OpenAI integration, PR summary & comments          |
| **GitHubService**    | Handles GitHub webhooks & PR data (via Octokit)    |
| **PrSummarizerTool** | File insights, change stats, complexity estimation |
| **CodeScannerTool**  | Code smells, debug logs, secret detection          |
| **PRReviewAgent**    | Orchestrates analysis & composes final review      |

### 🤖 Dependency Injection Setup

```csharp
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<CodeScannerTool>();
builder.Services.AddSingleton<LlmService>();
builder.Services.AddSingleton<PrSummarizerTool>();
builder.Services.AddSingleton<PrReviewAgent>();
```

---

## 🔄 Workflow

| Stage                     | What Happens                         |
| ------------------------- | ------------------------------------ |
| 📥 **Context Collection** | Fetch PR metadata & diff             |
| 🧪 **Analysis**           | Static scanning + diff understanding |
| 🧠 **LLM Review**         | AI transforms insights into feedback |
| 📝 **Feedback Posting**   | Comments appear on PR automatically  |

---

## 📊 Analysis Features

✔ File type classification
✔ Added/deleted line stats
✔ Complexity & effort estimation
✔ PR intent summary
✔ Review checklist

**Supported file types:**
→ Code files, configs, docs, tests, Docker/K8s files, CI configs

---

## 🚀 Getting Started

### 1️. Clone Repo

```bash
git clone <repository-url>
cd github-pr-assistant
```

### 2️. Restore Packages

```bash
dotnet restore
```

### 3️. Configure Secrets

Edit `appsettings.json` or use user secrets.

### 4️. Run App

```bash
dotnet build
dotnet run
```

### 5️. Add GitHub Webhook

| Setting | Value                                |
| ------- | ------------------------------------ |
| URL     | `http://your-app-url/webhook/github` |
| Events  | `Pull Request`                       |
| Secret  | same as config                       |

---

## 🔐 Security Best Practices

* Store PAT & OpenAI keys in **secure vaults**
* Use **environment variables** in production
* Validate GitHub webhook signatures
* Rotate tokens regularly

---

## 🤝 Contributing

1. Fork repo
2. Create feature branch
3. Commit & push
4. Submit Pull Request 🎉

---

## ⭐ Support the Project

If this project saves you time, consider starring it! ⭐


---
