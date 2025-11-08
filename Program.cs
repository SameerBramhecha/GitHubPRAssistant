using GitHubPRAssistant.Services;
using GitHubPRAssistant.Tools;
using GitHubPRAssistant.Agents;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<CodeScannerTool>();
builder.Services.AddSingleton<LlmService>();
builder.Services.AddSingleton<PrSummarizerTool>();
builder.Services.AddSingleton<PrReviewAgent>();


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
