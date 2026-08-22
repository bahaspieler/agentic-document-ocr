using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using OcrAgent.Agent;
using OcrAgent.Mcp;

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

    builder.Services
        .AddMcpServer(options => options.ServerInstructions = AgentInstructions.ForMcpServer)
        // Stateless is the SDK default from the 2026-07-28 protocol revision, and is stated here
        // because it is what lets the container app scale past one replica: no request depends on
        // having reached the replica that served an earlier one, so no session affinity is needed.
        .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools(tools)
        .WithPrompts(prompts);

    var app = builder.Build();
    app.MapMcp();
    await app.RunAsync();
}
