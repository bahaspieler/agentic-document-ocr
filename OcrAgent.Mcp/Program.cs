using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using ModelContextProtocol.AspNetCore;
using OcrAgent.Agent;
using OcrAgent.Mcp;
using OcrAgent.Mcp.Api;

// Two transports, one server:
//   default          -> ASP.NET Core Streamable HTTP (cloud-ready, reachable by URL)
//   --stdio          -> stdio transport (local MCP clients like Cursor / Claude Desktop)
bool useStdio = args.Contains("--stdio");

// In stdio mode, stdout carries the MCP protocol. Any stray Console.Write from library
// code would corrupt it, so redirect Console output to stderr before anything runs.
if (useStdio)
    Console.SetOut(Console.Error);

// Build the OcrAgent engine and its tool surface once; the same tools serve every request.
using var toolHost = OcrAgentToolHost.Create();
var tools = toolHost.CreateMcpTools().ToArray();
var prompts = toolHost.CreateMcpPrompts().ToArray();

if (useStdio)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Keep stdout clean: send all logs to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services
        .AddMcpServer(options => options.ServerInstructions = AgentInstructions.ForMcpServer)
        .WithStdioServerTransport()
        .WithTools(tools)
        .WithPrompts(prompts);

    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);
    var auth = toolHost.Configuration.GetSection("Auth").Get<AuthConfiguration>()
        ?? new AuthConfiguration();

    if (auth.Enabled && !string.IsNullOrWhiteSpace(auth.ClientId))
    {
        builder.Configuration["Auth:Instance"] = auth.Instance;
        builder.Configuration["Auth:TenantId"] = auth.TenantId;
        builder.Configuration["Auth:ClientId"] = auth.ClientId;
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("Auth"));
        builder.Services.AddAuthorization();
    }

    builder.Services
        .AddMcpServer(options => options.ServerInstructions = AgentInstructions.ForMcpServer)
        // Stateless is the SDK default from the 2026-07-28 protocol revision, and is stated here
        // because it is what lets the container app scale past one replica: no request depends on
        // having reached the replica that served an earlier one, so no session affinity is needed.
        .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools(tools)
        .WithPrompts(prompts);

    var app = builder.Build();
    if (auth.Enabled && !string.IsNullOrWhiteSpace(auth.ClientId))
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    AgentService? agentService = null;
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        agentService = AgentService.Create(toolHost.Aggregator.GetAllTools());

    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapOcrApi(toolHost, agentService, auth);
    app.MapMcp();
    app.MapFallbackToFile("index.html");
    await app.RunAsync();
}
