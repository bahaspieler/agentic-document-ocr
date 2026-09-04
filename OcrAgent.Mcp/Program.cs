using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using OcrAgent.Agent;
using OcrAgent.Mcp;
using OcrAgent.Mcp.Api;

namespace OcrAgent.Mcp;

/// <summary>Starts the OCR serving host with HTTP or stdio MCP transport.</summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        var useStdio = args.Contains("--stdio");

        // Stdout carries the MCP protocol in stdio mode.
        if (useStdio)
            Console.SetOut(Console.Error);

        using var toolHost = OcrAgentToolHost.Create();
        var tools = toolHost.CreateMcpTools().ToArray();
        var prompts = toolHost.CreateMcpPrompts().ToArray();

        if (useStdio)
            await RunStdioAsync(args, tools, prompts);
        else
            await RunHttpAsync(args, toolHost, tools, prompts);
    }

    #region Transport Hosts

    private static async Task RunStdioAsync(
        string[] args,
        IReadOnlyCollection<McpServerTool> tools,
        IReadOnlyCollection<McpServerPrompt> prompts)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = AgentInstructions.ForMcpServer)
            .WithStdioServerTransport()
            .WithTools(tools)
            .WithPrompts(prompts);

        using var app = builder.Build();
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("OcrAgent.Mcp")
            .LogInformation(
                "Starting MCP stdio host with {ToolCount} tools and {PromptCount} prompts.",
                tools.Count,
                prompts.Count);
        await app.RunAsync();
    }

    private static async Task RunHttpAsync(
        string[] args,
        OcrAgentToolHost toolHost,
        IReadOnlyCollection<McpServerTool> tools,
        IReadOnlyCollection<McpServerPrompt> prompts)
    {
        var builder = WebApplication.CreateBuilder(args);
        var auth = toolHost.Configuration.GetSection("Auth").Get<AuthConfiguration>()
            ?? new AuthConfiguration();
        auth.Validate();
        var authConfigured = auth.Enabled;

        ConfigureAuthentication(builder, auth, authConfigured);
        builder.Services
            .AddMcpServer(options => options.ServerInstructions = AgentInstructions.ForMcpServer)
            .AddAuthorizationFilters()
            // Stateless transport lets requests reach any replica without session affinity.
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools(tools)
            .WithPrompts(prompts);

        var app = builder.Build();
        if (authConfigured)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        var agentService = CreateAgentService(toolHost);
        LogConfiguration(app, toolHost, auth, authConfigured, agentService);

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapOcrApi(toolHost, agentService, auth);
        if (authConfigured)
            MapProtectedResourceMetadata(app, auth);
        var mcp = app.MapMcp();
        if (authConfigured)
            mcp.RequireAuthorization(AuthPolicies.Access);
        app.MapFallbackToFile("index.html");
        await app.RunAsync();
    }

    private static void MapProtectedResourceMetadata(
        WebApplication app,
        AuthConfiguration auth)
    {
        app.MapGet(
                "/.well-known/oauth-protected-resource",
                () => Results.Ok(new ProtectedResourceMetadata
                {
                    Resource = auth.ResourceUri,
                    AuthorizationServers = { auth.AuthorizationServer },
                    ScopesSupported = [auth.ScopeName],
                    ResourceName = "Hidden Layer OCR Agent",
                }))
            .AllowAnonymous();
    }

    #endregion

    #region Application Services

    private static void ConfigureAuthentication(
        WebApplicationBuilder builder,
        AuthConfiguration auth,
        bool authConfigured)
    {
        if (!authConfigured)
            return;

        var resourceUri = new Uri(auth.ResourceUri);
        var resourceMetadataUri = new Uri(
            resourceUri,
            "/.well-known/oauth-protected-resource");

        var authentication = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthSchemes.Composite;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            });

        authentication
            .AddPolicyScheme(
                AuthSchemes.Composite,
                "Auth0 access token or API key",
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(ApiKeyAuthenticationOptions.HeaderName)
                            ? AuthSchemes.ApiKey
                            : AuthSchemes.Jwt;
                })
            .AddJwtBearer(
                AuthSchemes.Jwt,
                options =>
                {
                    options.Authority = auth.AuthorizationServer;
                    options.Audience = auth.Audience;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.NameClaimType = "name";
                })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                AuthSchemes.ApiKey,
                options => options.KeyHashes = auth.ApiKeys);

        authentication.AddMcp(options =>
        {
            options.ResourceMetadataUri = resourceMetadataUri;
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = resourceUri.AbsoluteUri,
                AuthorizationServers = { auth.AuthorizationServer },
                ScopesSupported = [auth.ScopeName],
                ResourceName = "Hidden Layer OCR Agent",
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthPolicies.Access,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        context.User.HasClaim("auth_method", "api_key") ||
                        context.User
                            .FindAll("scope")
                            .SelectMany(claim => claim.Value.Split(
                                ' ',
                                StringSplitOptions.RemoveEmptyEntries))
                            .Contains(auth.ScopeName, StringComparer.Ordinal)));
        });
    }

    private static AgentService? CreateAgentService(OcrAgentToolHost toolHost) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
            ? null
            : AgentService.Create(toolHost.Aggregator.GetAllTools());

    private static void LogConfiguration(
        WebApplication app,
        OcrAgentToolHost toolHost,
        AuthConfiguration auth,
        bool authConfigured,
        AgentService? agentService)
    {
        app.Logger.LogInformation(
            "Starting OCR serving host. Storage={StorageProvider}, run store enabled={RunStoreEnabled}, " +
            "SQL enabled={SqlEnabled}, authentication enabled={AuthEnabled}, agent enabled={AgentEnabled}.",
            toolHost.Store.GetType().Name,
            toolHost.RunStore.IsEnabled,
            toolHost.SessionStore.IsEnabled,
            authConfigured,
            agentService is not null);

        if (agentService is null)
        {
            app.Logger.LogWarning(
                "OPENAI_API_KEY is not configured; agent conversation endpoints are disabled.");
        }
    }

    #endregion
}
