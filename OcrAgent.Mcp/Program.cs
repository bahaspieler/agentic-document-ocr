using Microsoft.Extensions.Logging;
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

if (useStdio)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Keep stdout clean: send all logs to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools(tools);

    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools(tools);

    var app = builder.Build();
    app.MapMcp();
    await app.RunAsync();
}
