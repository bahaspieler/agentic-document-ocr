using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using OcrAgent.Agent;
using OcrAgent.Runs;
using OcrAgent.Sql;

namespace OcrAgent.Mcp.Api;

public static class ApiEndpoints
{
    public static void MapOcrApi(
        this WebApplication app,
        OcrAgentToolHost host,
        AgentService? agentService,
        AuthConfiguration auth)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config", () => Results.Ok(new
        {
            auth = new
            {
                enabled = auth.Enabled,
                clientId = auth.ClientId,
                authority = $"{auth.Instance.TrimEnd('/')}/{auth.TenantId}"
            },
            sqlEnabled = host.SessionStore.IsEnabled,
            agentEnabled = agentService is not null
        }));

        api.MapPost("/images", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data is required." });

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "A non-empty 'file' is required." });
            if (file.Length > 20 * 1024 * 1024)
                return Results.BadRequest(new { error = "Images are limited to 20 MB." });

            var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
            if (extension is not ("png" or "jpg" or "jpeg" or "bmp" or "tif" or "tiff" or "webp"))
                return Results.BadRequest(new { error = "Unsupported image type." });

            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            using var source = RunScope.EnterSource("rest");
            var reference = host.Store.SaveBytes(
                stream.ToArray(), extension, desiredReference: form["reference"].FirstOrDefault());
            return Results.Ok(new { reference });
        }).DisableAntiforgery();

        api.MapGet("/workflows", () =>
            Results.Ok(host.Aggregator.Workflows.Catalog.List().Select(workflow => new
            {
                id = workflow.Id,
                name = workflow.Name,
                description = workflow.Description
            })));

        api.MapPost("/jobs", async (JobRequest request) =>
        {
            using var source = RunScope.EnterSource("rest");
            if (request.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.ImageReference))
                    return Results.BadRequest(new { error = "imageReference is required for a workflow." });
                var output = await host.Aggregator.Workflows.RunWorkflow(
                    request.Name, request.ImageReference, request.OverridesJson);
                return Results.Ok(new { output });
            }

            if (!request.Mode.Equals("tool", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "mode must be 'tool' or 'workflow'." });

            var function = host.Aggregator.GetAllTools()
                .OfType<AIFunction>()
                .FirstOrDefault(candidate => candidate.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
            if (function is null)
                return Results.NotFound(new { error = $"Tool '{request.Name}' was not found." });

            var arguments = new AIFunctionArguments();
            if (request.Arguments is not null)
            {
                foreach (var (name, value) in request.Arguments)
                    arguments[name] = JsonSerializer.Deserialize<object?>(value.GetRawText());
            }
            if (!string.IsNullOrWhiteSpace(request.ImageReference) &&
                !arguments.ContainsKey("imageReference"))
                arguments["imageReference"] = request.ImageReference;

            var result = await function.InvokeAsync(arguments);
            return Results.Ok(new { output = result?.ToString() ?? "" });
        });

        api.MapGet("/runs", async (
            int? top, string? mode, string? conversationId, CancellationToken cancellationToken) =>
        {
            if (!host.RunStore.IsEnabled)
                return Results.Ok(Array.Empty<RunRecord>());
            var rows = await host.RunStore.ListAsync(
                Math.Clamp(top ?? 50, 1, 200), mode, cancellationToken);
            return Results.Ok(string.IsNullOrWhiteSpace(conversationId)
                ? rows
                : rows.Where(row => row.ConversationId == conversationId));
        });

        api.MapGet("/runs/{runId}", async (string runId, CancellationToken cancellationToken) =>
        {
            var run = await host.RunStore.GetAsync(runId, cancellationToken);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        api.MapGet("/artifacts/{**reference}", (string reference) =>
        {
            if (string.IsNullOrWhiteSpace(reference) || !host.Store.Exists(reference))
                return Results.NotFound();
            var path = host.Store.GetImagePath(reference);
            return Results.File(path, ContentTypeFor(path), enableRangeProcessing: true);
        });

        api.MapGet("/receipts", async (
            string? merchant, DateTime? from, DateTime? to, int? top,
            CancellationToken cancellationToken) =>
        {
            var rows = await host.ReceiptStore.QueryAsync(
                merchant, from, to, top ?? 100, cancellationToken);
            return Results.Ok(rows.Select(receipt => new
            {
                receipt.ReceiptId,
                receipt.RunId,
                receipt.ConversationId,
                receipt.Merchant,
                receipt.TransactionUtc,
                receipt.Currency,
                receipt.Subtotal,
                receipt.Tax,
                receipt.Total,
                receipt.IsValid,
                receipt.ValidationScore,
                receipt.CreatedUtc
            }));
        });

        api.MapGet("/receipts/{receiptId}", async (
            string receiptId, CancellationToken cancellationToken) =>
        {
            var receipt = await host.ReceiptStore.GetAsync(receiptId, cancellationToken);
            return receipt is null ? Results.NotFound() : Results.Ok(receipt);
        });

        api.MapPost("/conversations", async (
            CreateConversationRequest request,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            if (!host.SessionStore.IsEnabled)
                return SqlRequired();
            var userId = await UpsertUserAsync(host.UserStore, principal, cancellationToken);
            var id = RunId.NewId();
            var title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title;
            var conversation = await host.SessionStore.CreateAsync(
                id, userId, title!, cancellationToken);
            return Results.Created($"/api/conversations/{id}", ToPublicConversation(conversation));
        });

        api.MapGet("/conversations", async (
            ClaimsPrincipal principal, int? top, CancellationToken cancellationToken) =>
        {
            if (!host.SessionStore.IsEnabled)
                return SqlRequired();
            var userId = await UpsertUserAsync(host.UserStore, principal, cancellationToken);
            if (userId is null)
                return Results.Unauthorized();
            var conversations = await host.SessionStore.ListAsync(
                userId, top ?? 100, cancellationToken);
            return Results.Ok(conversations.Select(ToPublicConversation));
        });

        api.MapGet("/conversations/{conversationId}", async (
            string conversationId,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(principal);
            var denied = await CheckConversationAsync(
                host.SessionStore, conversationId, userId, cancellationToken);
            if (denied is not null)
                return denied;
            var conversation = await host.SessionStore.GetAsync(conversationId, cancellationToken);
            var turns = await host.SessionStore.ListTurnsAsync(conversationId, cancellationToken);
            return Results.Ok(new { conversation = ToPublicConversation(conversation!), turns });
        });

        api.MapPatch("/conversations/{conversationId}", async (
            string conversationId,
            RenameConversationRequest request,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(principal);
            var denied = await CheckConversationAsync(
                host.SessionStore, conversationId, userId, cancellationToken);
            if (denied is not null)
                return denied;
            await host.SessionStore.RenameAsync(
                conversationId, request.Title, userId, cancellationToken);
            return Results.NoContent();
        });

        api.MapDelete("/conversations/{conversationId}", async (
            string conversationId,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(principal);
            var denied = await CheckConversationAsync(
                host.SessionStore, conversationId, userId, cancellationToken);
            if (denied is not null)
                return denied;
            await host.SessionStore.DeleteAsync(conversationId, userId, cancellationToken);
            return Results.NoContent();
        });

        api.MapPost("/conversations/claim", async (
            ClaimConversationsRequest request,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            if (!host.SessionStore.IsEnabled)
                return SqlRequired();
            var userId = await UpsertUserAsync(host.UserStore, principal, cancellationToken);
            if (userId is null)
                return Results.Unauthorized();
            await host.SessionStore.ClaimAsync(request.ConversationIds, userId, cancellationToken);
            return Results.NoContent();
        });

        api.MapPost("/conversations/{conversationId}/messages", async (
            string conversationId,
            ChatMessageRequest request,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!host.SessionStore.IsEnabled)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Azure SQL is not configured." }, cancellationToken);
                return;
            }
            if (agentService is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(
                    new { error = "OPENAI_API_KEY is not configured." }, cancellationToken);
                return;
            }
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { error = "message is required." }, cancellationToken);
                return;
            }

            var userId = GetUserId(context.User);
            var stored = await host.SessionStore.GetAsync(conversationId, cancellationToken);
            if (stored is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (stored.OwnerUserId is not null && stored.OwnerUserId != userId)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var idempotencyKey = request.IdempotencyKey
                ?? context.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { error = "An idempotencyKey is required." }, cancellationToken);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Append("X-Accel-Buffering", "no");

            var previousTurn = await host.SessionStore.GetTurnAsync(
                conversationId, idempotencyKey, cancellationToken);
            if (previousTurn is not null)
            {
                await WriteSseAsync(context.Response, "token",
                    new { text = previousTurn.Answer }, cancellationToken);
                await WriteSseAsync(context.Response, "done",
                    new { answer = previousTurn.Answer, replayed = true }, cancellationToken);
                return;
            }

            var channel = Channel.CreateUnbounded<SseEnvelope>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var producer = ProduceTurnAsync(
                host, agentService, stored, request.Message, idempotencyKey,
                channel.Writer, cancellationToken);

            await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken))
                await WriteSseAsync(context.Response, envelope.Event, envelope.Data, cancellationToken);
            await producer;
        });
    }

    private static async Task ProduceTurnAsync(
        OcrAgentToolHost host,
        AgentService agentService,
        ConversationRecord stored,
        string message,
        string idempotencyKey,
        ChannelWriter<SseEnvelope> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var conversation = string.IsNullOrWhiteSpace(stored.PayloadJson)
                ? await agentService.StartConversationAsync(stored.Id, cancellationToken)
                : await agentService.RestoreConversationAsync(
                    stored.Id, stored.PayloadJson, cancellationToken);

            using var source = RunScope.EnterSource("rest");
            var answer = await agentService.RunTurnStreamingAsync(
                message,
                conversation,
                streamEvent => writer.TryWrite(new SseEnvelope(streamEvent.Type, streamEvent)),
                cancellationToken);
            var payload = await agentService.SerializeConversationAsync(
                conversation, cancellationToken);
            await host.SessionStore.SaveTurnAsync(
                stored, payload, idempotencyKey, message, answer, cancellationToken);

            var recentRuns = host.RunStore.IsEnabled
                ? await host.RunStore.ListAsync(100, cancellationToken: cancellationToken)
                : [];
            var runIds = recentRuns
                .Where(run => run.ConversationId == stored.Id)
                .Select(run => run.RunId)
                .ToArray();
            writer.TryWrite(new SseEnvelope("done", new { answer, runIds, replayed = false }));
        }
        catch (ConversationConcurrencyException exception)
        {
            writer.TryWrite(new SseEnvelope("error", new
            {
                code = "conversation_conflict",
                message = exception.Message
            }));
        }
        catch (Exception exception)
        {
            writer.TryWrite(new SseEnvelope("error", new
            {
                code = "turn_failed",
                message = exception.Message
            }));
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task<IResult?> CheckConversationAsync(
        ISessionStore store,
        string conversationId,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (!store.IsEnabled)
            return SqlRequired();
        var conversation = await store.GetAsync(conversationId, cancellationToken);
        if (conversation is null)
            return Results.NotFound();
        return conversation.OwnerUserId is not null && conversation.OwnerUserId != userId
            ? Results.Forbid()
            : null;
    }

    private static async Task<string?> UpsertUserAsync(
        IUserStore store, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return null;
        await store.UpsertAsync(
            userId,
            principal.FindFirstValue("tid") ?? "microsoft",
            principal.FindFirstValue("name") ?? principal.Identity?.Name,
            cancellationToken);
        return userId;
    }

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue("oid")
              ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? principal.FindFirstValue("sub")
            : null;

    private static IResult SqlRequired() =>
        Results.Problem(
            "Azure SQL is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static object ToPublicConversation(ConversationRecord conversation) => new
    {
        conversation.Id,
        conversation.OwnerUserId,
        conversation.Title,
        conversation.CreatedUtc,
        conversation.LastActiveUtc,
        conversation.TurnCount
    };

    private static async Task WriteSseAsync(
        HttpResponse response,
        string eventName,
        object data,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private sealed record SseEnvelope(string Event, object Data);
}
