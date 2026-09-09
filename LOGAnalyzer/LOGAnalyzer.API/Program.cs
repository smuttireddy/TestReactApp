using System.Collections.Concurrent;
using System.Threading.Channels;
using LOGAnalyzer.API.BackgroundServices;
using LOGAnalyzer.Core.DTOs;
using LOGAnalyzer.Core.Interfaces;
using LOGAnalyzer.Core.Settings;
using LOGAnalyzer.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────
//  MVC + API
// ─────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "LOG Analyzer API",
        Version     = "v1",
        Description = "Upload .log/.txt files, receive AI-analyzed JSON output and Excel export."
    });
});

// ─────────────────────────────────────────────
//  IBM watsonx.ai settings
// ─────────────────────────────────────────────
builder.Services.Configure<WatsonXSettings>(builder.Configuration.GetSection("WatsonX"));

// ─────────────────────────────────────────────
//  HttpClient with timeout for watsonx calls
// ─────────────────────────────────────────────
builder.Services.AddHttpClient("watsonx", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient(); // default client for IAM token

// ─────────────────────────────────────────────
//  Domain services
// ─────────────────────────────────────────────
builder.Services.AddSingleton<ILogParserService, LogParserService>();

// Rule-based fallback service (concrete, not interface)
builder.Services.AddSingleton<LogAnalysisService>();

// watsonx-powered service as the primary ILogAnalysisService
builder.Services.AddSingleton<ILogAnalysisService>(sp =>
{
    var settings     = sp.GetRequiredService<IOptions<WatsonXSettings>>();
    var httpFactory  = sp.GetRequiredService<IHttpClientFactory>();
    var fallback     = sp.GetRequiredService<LogAnalysisService>();
    var logger       = sp.GetRequiredService<ILoggerFactory>()
                         .CreateLogger<WatsonXLogAnalysisService>();
    return new WatsonXLogAnalysisService(settings, httpFactory, fallback, logger);
});

builder.Services.AddSingleton<IExcelExportService, ExcelExportService>();

// ─────────────────────────────────────────────
//  Background job infrastructure
// ─────────────────────────────────────────────
builder.Services.AddSingleton<ConcurrentDictionary<string, LogAnalysisResponse>>();

var channel = Channel.CreateUnbounded<LogJob>(new UnboundedChannelOptions
{
    SingleReader = true
});
builder.Services.AddSingleton(channel);

builder.Services.AddHostedService<LogProcessingBackgroundService>();

// ─────────────────────────────────────────────
//  File upload size limit (10 MB)
// ─────────────────────────────────────────────
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

var app = builder.Build();

// ─────────────────────────────────────────────
//  Middleware pipeline
// ─────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "LOG Analyzer API v1"));
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Log}/{action=Upload}/{id?}");

app.Run();
