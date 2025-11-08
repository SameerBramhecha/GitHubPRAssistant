using GitHubPRAssistant.Agents;
using GitHubPRAssistant.Services;
using GitHubPRAssistant.Tools;
using Microsoft.Extensions.DependencyInjection;
using Octokit;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register GitHub client first
builder.Services.AddSingleton<IGitHubClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["GitHub:Token"] ??
        throw new InvalidOperationException("GitHub token not configured");

    var client = new GitHubClient(new ProductHeaderValue("AI-PR-Assistant"))
    {
        Credentials = new Credentials(token)
    };

    return client;
});

// Register services in dependency order
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<LlmService>();
builder.Services.AddSingleton<CodeScannerTool>();
builder.Services.AddSingleton<PrSummarizerTool>();
builder.Services.AddSingleton<AutomatedActionsService>();
builder.Services.AddSingleton<PrReviewAgent>();

// Configure CORS (if needed)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
