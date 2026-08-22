using System.Security.Claims;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using OcrAgent.Agent;
using OcrAgent.Extraction.Models;
using OcrAgent.Runs;
using OcrAgent.Sql;
using OcrAgent.Storage;
using OcrAgent.Workflows.Model;

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
            agentEnabled = agentService is not null,
            model = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT")
                ?? "gpt-4o-mini"
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

        api.MapPost("/jobs", async (
            JobRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            using var source = RunScope.EnterSource("rest");
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                var denied = await CheckConversationAsync(
                    host.SessionStore, request.ConversationId, GetUserId(principal), default);
                if (denied is not null)
                    return denied;
            }
            using var conversationScope = string.IsNullOrWhiteSpace(request.ConversationId)
                ? null
                : RunScope.EnterConversation(request.ConversationId!);
            if (request.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.ImageReference))
                    return Results.BadRequest(new { error = "imageReference is required for a workflow." });
                var output = await host.Aggregator.Workflows.RunWorkflowDetailed(
                    request.Name, request.ImageReference, request.OverridesJson);
                if (host.ReceiptStore.IsEnabled)
                {
                    var receipts = await host.ReceiptStore.QueryAsync(top: 200);
                    output.ReceiptId = receipts.FirstOrDefault(
                        receipt => receipt.RunId == output.RunId)?.ReceiptId
                        ?? output.ReceiptId;
                }
                var run = await host.RunStore.GetAsync(output.RunId, cancellationToken)
                    ?? ToRunRecord(output, request.ConversationId);
                var artifacts = await DescribeRunArtifactsAsync(
                    host.Store, run, cancellationToken);
                if (!string.IsNullOrWhiteSpace(request.ConversationId))
                {
                    await PersistJobTurnAsync(
                        host.SessionStore,
                        request,
                        run,
                        artifacts,
                        GetUserId(principal),
                        cancellationToken);
                }
                return Results.Ok(new { result = output, artifacts });
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

        api.MapPost("/jobs/stream", async (
            HttpContext context,
            JobRequest request,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            if (!request.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(request.ImageReference))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { error = "A workflow mode and imageReference are required." },
                    cancellationToken);
                return;
            }
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                var denied = await CheckConversationAsync(
                    host.SessionStore,
                    request.ConversationId,
                    GetUserId(principal),
                    cancellationToken);
                if (denied is not null)
                {
                    await denied.ExecuteAsync(context);
                    return;
                }
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Append("X-Accel-Buffering", "no");

            var channel = Channel.CreateUnbounded<SseEnvelope>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var producer = ProduceWorkflowJobAsync(
                host,
                request,
                GetUserId(principal),
                channel.Writer,
                cancellationToken);
            await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken))
                await WriteSseAsync(
                    context.Response,
                    envelope.Event,
                    envelope.Data,
                    cancellationToken);
            await producer;
        });

        api.MapGet("/runs", async (
            int? top, string? mode, string? conversationId, string? name, string? status,
            string? source, DateTimeOffset? from, DateTimeOffset? to, string? cursor,
            ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            if (!host.RunStore.IsEnabled)
                return Results.Ok(Array.Empty<RunRecord>());
            var rows = await host.RunStore.ListAsync(
                1000, mode, cancellationToken);
            var visible = new List<RunRecord>();
            foreach (var row in rows)
            {
                if (await CanAccessConversationLinkAsync(
                    host.SessionStore, row.ConversationId, conversationId,
                    principal, cancellationToken))
                    visible.Add(row);
            }
            var filtered = visible.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(conversationId))
                filtered = filtered.Where(row => row.ConversationId == conversationId);
            if (!string.IsNullOrWhiteSpace(name))
                filtered = filtered.Where(row =>
                    row.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (status?.Equals("success", StringComparison.OrdinalIgnoreCase) == true)
                filtered = filtered.Where(row => row.Success);
            else if (status?.Equals("failed", StringComparison.OrdinalIgnoreCase) == true)
                filtered = filtered.Where(row => !row.Success);
            if (!string.IsNullOrWhiteSpace(source))
                filtered = filtered.Where(row =>
                    row.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
            if (from.HasValue)
                filtered = filtered.Where(row => row.StartTimeUtc >= from.Value);
            if (to.HasValue)
                filtered = filtered.Where(row => row.StartTimeUtc < to.Value);
            if (!string.IsNullOrWhiteSpace(cursor))
                filtered = filtered.SkipWhile(row => row.RunId != cursor).Skip(1);
            return Results.Ok(filtered.Take(Math.Clamp(top ?? 50, 1, 200)));
        });

        api.MapGet("/runs/{runId}", async (
            string runId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var run = await host.RunStore.GetAsync(runId, cancellationToken);
            if (run is null)
                return Results.NotFound();
            var denied = await CheckRunAccessAsync(
                host.SessionStore, run, conversationId, principal, cancellationToken);
            return denied ?? Results.Ok(run);
        });

        api.MapGet("/runs/{runId}/artifacts", async (
            string runId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var run = await host.RunStore.GetAsync(runId, cancellationToken);
            if (run is null)
                return Results.NotFound();
            var denied = await CheckRunAccessAsync(
                host.SessionStore, run, conversationId, principal, cancellationToken);
            if (denied is not null)
                return denied;
            return Results.Ok(await DescribeRunArtifactsAsync(
                host.Store, run, cancellationToken));
        });

        api.MapGet("/runs/{runId}/artifacts/{name}", async (
            string runId, string name, bool? download, string? conversationId,
            ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var run = await host.RunStore.GetAsync(runId, cancellationToken);
            if (run is null)
                return Results.NotFound();
            var denied = await CheckRunAccessAsync(
                host.SessionStore, run, conversationId, principal, cancellationToken);
            if (denied is not null)
                return denied;
            if (string.IsNullOrWhiteSpace(run.OutputDirectory))
            {
                if (string.IsNullOrWhiteSpace(run.OutputReference) ||
                    !host.Store.Exists(run.OutputReference))
                    return Results.NotFound();
                var path = host.Store.GetImagePath(run.OutputReference);
                if (!Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return Results.NotFound();
                return Results.File(
                    path,
                    ContentTypeFor(path),
                    fileDownloadName: download == true ? Path.GetFileName(path) : null,
                    enableRangeProcessing: true);
            }
            if (host.Store is not IArtifactStore artifacts)
                return Results.NotFound();
            try
            {
                var stream = await artifacts.OpenReadAsync(
                    run.OutputDirectory, name, cancellationToken);
                return Results.Stream(
                    stream,
                    ContentTypeFor(name),
                    fileDownloadName: download == true ? name : null,
                    enableRangeProcessing: true);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "Invalid artifact name." });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
        });

        api.MapGet("/artifacts/{**reference}", async (
            string reference, string? conversationId, bool? download, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(reference) || !host.Store.Exists(reference))
                return Results.NotFound();
            if (host.RunStore.IsEnabled)
            {
                var runs = await host.RunStore.ListAsync(
                    1000, cancellationToken: cancellationToken);
                var linked = runs.FirstOrDefault(run =>
                    run.OutputReference == reference ||
                    (run.OutputDirectory is not null &&
                     reference.StartsWith(
                         run.OutputDirectory.Replace('\\', '/') + "/",
                         StringComparison.Ordinal)));
                if (linked is not null)
                {
                    var denied = await CheckRunAccessAsync(
                        host.SessionStore, linked, conversationId, principal, cancellationToken);
                    if (denied is not null)
                        return denied;
                }
            }
            var path = host.Store.GetImagePath(reference);
            return Results.File(
                path,
                ContentTypeFor(path),
                fileDownloadName: download == true ? Path.GetFileName(path) : null,
                enableRangeProcessing: true);
        });

        api.MapGet("/receipts", async (
            string? merchant, DateTime? from, DateTime? to, int? top,
            string? conversationId, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var rows = await host.ReceiptStore.QueryAsync(
                merchant, from, to, top ?? 100, cancellationToken);
            var visible = new List<ReceiptRecord>();
            foreach (var receipt in rows)
            {
                if (await CanAccessConversationLinkAsync(
                    host.SessionStore, receipt.ConversationId, conversationId,
                    principal, cancellationToken))
                    visible.Add(receipt);
            }
            return Results.Ok(visible.Select(receipt => new
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
                receipt.OcrConfidence,
                receipt.IsValid,
                receipt.ValidationScore,
                receipt.ReviewStatus,
                receipt.CurrentRevision,
                receipt.CreatedUtc,
                receipt.UpdatedUtc
            }));
        });

        api.MapGet("/receipts/{receiptId}", async (
            string receiptId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var receipt = await host.ReceiptStore.GetAsync(receiptId, cancellationToken);
            if (receipt is null)
                return Results.NotFound();
            var denied = await CheckReceiptAccessAsync(
                host.SessionStore, receipt, conversationId, principal, cancellationToken);
            return denied ?? Results.Ok(receipt);
        });

        api.MapPatch("/receipts/{receiptId}", async (
            string receiptId, ReceiptUpdateRequest request, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var existing = await host.ReceiptStore.GetAsync(receiptId, cancellationToken);
            if (existing is null)
                return Results.NotFound();
            var denied = await CheckReceiptAccessAsync(
                host.SessionStore, existing, request.ConversationId, principal, cancellationToken);
            if (denied is not null)
                return denied;
            byte[] rowVersion;
            try { rowVersion = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "rowVersion must be base64." });
            }
            try
            {
                var editor = GetUserId(principal) ??
                    $"anonymous:{request.ConversationId ?? existing.ConversationId}";
                var updated = await host.ReceiptStore.UpdateAsync(
                    receiptId, request.Receipt, rowVersion, editor,
                    request.ChangeSummary, request.Verified, cancellationToken);
                return Results.Ok(updated);
            }
            catch (ReceiptConcurrencyException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        api.MapGet("/receipts/{receiptId}/revisions", async (
            string receiptId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
        {
            var receipt = await host.ReceiptStore.GetAsync(receiptId, cancellationToken);
            if (receipt is null)
                return Results.NotFound();
            var denied = await CheckReceiptAccessAsync(
                host.SessionStore, receipt, conversationId, principal, cancellationToken);
            return denied ?? Results.Ok(
                await host.ReceiptStore.ListRevisionsAsync(receiptId, cancellationToken));
        });

        api.MapGet("/receipts/{receiptId}/original", async (
            string receiptId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
            await ReceiptExportAsync(
                host, receiptId, conversationId, principal, original: true,
                csv: false, cancellationToken));
        api.MapGet("/receipts/{receiptId}/corrected.json", async (
            string receiptId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
            await ReceiptExportAsync(
                host, receiptId, conversationId, principal, original: false,
                csv: false, cancellationToken));
        api.MapGet("/receipts/{receiptId}/corrected.csv", async (
            string receiptId, string? conversationId, ClaimsPrincipal principal,
            CancellationToken cancellationToken) =>
            await ReceiptExportAsync(
                host, receiptId, conversationId, principal, original: false,
                csv: true, cancellationToken));

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
            return Results.Ok(new
            {
                conversation = ToPublicConversation(conversation!),
                turns = turns.Select(ToPublicTurn)
            });
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
            if (string.IsNullOrWhiteSpace(request.Message) &&
                (request.Attachments is null || request.Attachments.Count == 0))
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
                foreach (var activity in DeserializeEvents(previousTurn.EventsJson))
                    await WriteSseAsync(context.Response, activity.Type, activity, cancellationToken);
                await WriteSseAsync(context.Response, "token",
                    new { text = previousTurn.Answer }, cancellationToken);
                await WriteSseAsync(context.Response, "done",
                    new
                    {
                        answer = previousTurn.Answer,
                        runIds = DeserializeStrings(previousTurn.RunIdsJson),
                        replayed = true
                    }, cancellationToken);
                return;
            }

            var channel = Channel.CreateUnbounded<SseEnvelope>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var producer = ProduceTurnAsync(
                host, agentService, stored, request, idempotencyKey,
                channel.Writer, cancellationToken);

            await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken))
                await WriteSseAsync(context.Response, envelope.Event, envelope.Data, cancellationToken);
            await producer;
        });
    }

    private static async Task ProduceWorkflowJobAsync(
        OcrAgentToolHost host,
        JobRequest request,
        string? userId,
        ChannelWriter<SseEnvelope> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var source = RunScope.EnterSource("rest");
            using var conversationScope = string.IsNullOrWhiteSpace(request.ConversationId)
                ? null
                : RunScope.EnterConversation(request.ConversationId);
            var activity = new List<AgentStreamEvent>();
            var progress = new CallbackProgress<WorkflowStepProgress>(step =>
            {
                var progressEvent = AgentStreamEvent.WorkflowStep(step);
                activity.Add(progressEvent);
                writer.TryWrite(new SseEnvelope("step", progressEvent));
            });
            var output = await host.Aggregator.Workflows.RunWorkflowDetailed(
                request.Name,
                request.ImageReference!,
                request.OverridesJson,
                progress);
            if (host.ReceiptStore.IsEnabled)
            {
                var receipts = await host.ReceiptStore.QueryAsync(top: 200, cancellationToken: cancellationToken);
                output.ReceiptId = receipts.FirstOrDefault(
                    receipt => receipt.RunId == output.RunId)?.ReceiptId
                    ?? output.ReceiptId;
            }
            var run = await host.RunStore.GetAsync(output.RunId, cancellationToken)
                ?? ToRunRecord(output, request.ConversationId);
            var artifacts = await DescribeRunArtifactsAsync(
                host.Store, run, cancellationToken);

            var runEvent = AgentStreamEvent.RunCompleted(run);
            writer.TryWrite(new SseEnvelope("run", runEvent));
            foreach (var artifact in artifacts)
                writer.TryWrite(new SseEnvelope(
                    "artifact",
                    AgentStreamEvent.ArtifactAvailable(artifact)));

            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                await PersistJobTurnAsync(
                    host.SessionStore,
                    request,
                    run,
                    artifacts,
                    userId,
                    cancellationToken,
                    activity);
            }
            writer.TryWrite(new SseEnvelope(
                "done",
                new { result = output, artifacts }));
        }
        catch (Exception ex)
        {
            writer.TryWrite(new SseEnvelope(
                "error",
                new { message = ex.Message }));
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task ProduceTurnAsync(
        OcrAgentToolHost host,
        AgentService agentService,
        ConversationRecord stored,
        ChatMessageRequest request,
        string idempotencyKey,
        ChannelWriter<SseEnvelope> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var attachments = request.Attachments ?? [];
            foreach (var attachment in attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.Reference) ||
                    !host.Store.Exists(attachment.Reference))
                    throw new ArgumentException($"Image attachment '{attachment.Reference}' was not found.");
            }

            var message = BuildAgentMessage(request.Message, attachments);
            var conversation = string.IsNullOrWhiteSpace(stored.PayloadJson)
                ? await agentService.StartConversationAsync(stored.Id, cancellationToken)
                : await agentService.RestoreConversationAsync(
                    stored.Id, stored.PayloadJson, cancellationToken);

            using var source = RunScope.EnterSource("rest");
            using var turnScope = RunScope.EnterTurn(idempotencyKey);
            var activity = new List<AgentStreamEvent>();
            var answer = await agentService.RunTurnStreamingAsync(
                message,
                conversation,
                streamEvent =>
                {
                    if (streamEvent.Type != "token")
                        activity.Add(streamEvent);
                    writer.TryWrite(new SseEnvelope(streamEvent.Type, streamEvent));
                },
                cancellationToken);
            foreach (var run in activity
                .Where(item => item.Run is not null)
                .Select(item => item.Run!)
                .ToArray())
            {
                foreach (var artifact in await DescribeRunArtifactsAsync(
                    host.Store, run, cancellationToken))
                {
                    var artifactEvent = AgentStreamEvent.ArtifactAvailable(artifact);
                    activity.Add(artifactEvent);
                    writer.TryWrite(new SseEnvelope("artifact", artifactEvent));
                }
            }
            var payload = await agentService.SerializeConversationAsync(
                conversation, cancellationToken);
            var runIds = activity
                .Where(item => item.Run is not null)
                .Select(item => item.Run!.RunId)
                .Distinct()
                .ToArray();
            await host.SessionStore.SaveTurnAsync(
                stored,
                payload,
                idempotencyKey,
                request.Message,
                answer,
                JsonSerializer.Serialize(activity, JsonOptions),
                JsonSerializer.Serialize(runIds, JsonOptions),
                JsonSerializer.Serialize(attachments, JsonOptions),
                cancellationToken);
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

    private static async Task PersistJobTurnAsync(
        ISessionStore sessions,
        JobRequest request,
        RunRecord run,
        IReadOnlyList<AgentArtifact> artifacts,
        string? userId,
        CancellationToken cancellationToken,
        IReadOnlyList<AgentStreamEvent>? precedingEvents = null)
    {
        if (!sessions.IsEnabled || string.IsNullOrWhiteSpace(request.ConversationId))
            return;

        var stored = await sessions.GetAsync(request.ConversationId, cancellationToken);
        if (stored is null)
            return;

        var activity = precedingEvents?.ToList() ?? [];
        activity.Add(AgentStreamEvent.RunCompleted(run));
        activity.AddRange(artifacts.Select(AgentStreamEvent.ArtifactAvailable));
        var attachment = new ChatAttachment(
            run.InputReference,
            request.AttachmentName ?? "Input image",
            request.AttachmentContentType);
        var userMessage = string.IsNullOrWhiteSpace(request.UserMessage)
            ? $"Run the guided workflow `{run.Name}` on this image."
            : request.UserMessage.Trim();
        var answer = run.Success
            ? $"Workflow **{run.Name}** completed successfully in {run.DurationMs / 1000d:F1} seconds. " +
              $"It produced {artifacts.Count} artifacts. Use the result card below for key files, " +
              "or open **View all files in run history**."
            : $"Workflow **{run.Name}** failed: {run.Error ?? "Unknown error"}";

        async Task SaveAsync(ConversationRecord expected)
        {
            _ = await sessions.SaveTurnAsync(
                expected,
                expected.PayloadJson ?? "",
                request.IdempotencyKey ?? run.RunId,
                userMessage,
                answer,
                JsonSerializer.Serialize(activity, JsonOptions),
                JsonSerializer.Serialize(new[] { run.RunId }, JsonOptions),
                JsonSerializer.Serialize(new[] { attachment }, JsonOptions),
                cancellationToken);
        }

        try
        {
            await SaveAsync(stored);
        }
        catch (ConversationConcurrencyException)
        {
            var refreshed = await sessions.GetAsync(stored.Id, cancellationToken);
            if (refreshed is null)
                throw;
            await SaveAsync(refreshed);
        }

        if (stored.Title.Equals("New conversation", StringComparison.OrdinalIgnoreCase))
            await sessions.RenameAsync(
                stored.Id, $"Workflow: {run.Name}", userId, cancellationToken);
    }

    private static RunRecord ToRunRecord(
        WorkflowRunResult result, string? conversationId) => new()
    {
        RunId = result.RunId,
        Mode = "workflow",
        Name = result.WorkflowId,
        Version = result.Version,
        Source = "rest",
        ConversationId = conversationId,
        InputReference = result.InputImageRef,
        OutputReference = result.OutputImageRef,
        OutputDirectory = result.OutputDirectory,
        StepCount = result.Trace.Count,
        Success = result.Success,
        Error = result.Error,
        DurationMs = result.TotalDurationMs,
        EndTimeUtc = DateTimeOffset.UtcNow,
        StartTimeUtc = DateTimeOffset.UtcNow.AddMilliseconds(-result.TotalDurationMs),
        TraceJson = JsonSerializer.Serialize(result.Trace, JsonOptions)
    };

    private static async Task<IResult?> CheckRunAccessAsync(
        ISessionStore sessions,
        RunRecord run,
        string? possessionConversationId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.ConversationId))
            return null;
        if (!sessions.IsEnabled)
            return SqlRequired();
        var conversation = await sessions.GetAsync(run.ConversationId, cancellationToken);
        if (conversation is null)
            return Results.NotFound();
        var userId = GetUserId(principal);
        if (conversation.OwnerUserId is not null)
            return conversation.OwnerUserId == userId ? null : Results.Forbid();
        return possessionConversationId == conversation.Id ? null : Results.Forbid();
    }

    private static Task<IResult?> CheckReceiptAccessAsync(
        ISessionStore sessions,
        ReceiptRecord receipt,
        string? possessionConversationId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        CheckRunOrReceiptConversationAsync(
            sessions, receipt.ConversationId, possessionConversationId,
            principal, cancellationToken);

    private static async Task<IResult?> CheckRunOrReceiptConversationAsync(
        ISessionStore sessions,
        string? linkedConversationId,
        string? possessionConversationId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(linkedConversationId))
            return null;
        if (!sessions.IsEnabled)
            return SqlRequired();
        var conversation = await sessions.GetAsync(linkedConversationId, cancellationToken);
        if (conversation is null)
            return Results.NotFound();
        var userId = GetUserId(principal);
        if (conversation.OwnerUserId is not null)
            return conversation.OwnerUserId == userId ? null : Results.Forbid();
        return possessionConversationId == linkedConversationId ? null : Results.Forbid();
    }

    private static async Task<bool> CanAccessConversationLinkAsync(
        ISessionStore sessions,
        string? linkedConversationId,
        string? possessionConversationId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(linkedConversationId))
            return true;
        if (!sessions.IsEnabled)
            return false;
        var conversation = await sessions.GetAsync(linkedConversationId, cancellationToken);
        if (conversation is null)
            return false;
        var userId = GetUserId(principal);
        return conversation.OwnerUserId is not null
            ? conversation.OwnerUserId == userId
            : possessionConversationId == linkedConversationId;
    }

    private static async Task<IResult> ReceiptExportAsync(
        OcrAgentToolHost host,
        string receiptId,
        string? conversationId,
        ClaimsPrincipal principal,
        bool original,
        bool csv,
        CancellationToken cancellationToken)
    {
        var record = await host.ReceiptStore.GetAsync(receiptId, cancellationToken);
        if (record is null)
            return Results.NotFound();
        var denied = await CheckReceiptAccessAsync(
            host.SessionStore, record, conversationId, principal, cancellationToken);
        if (denied is not null)
            return denied;
        var json = original ? record.RawJson : record.CurrentJson;
        if (!csv)
            return Results.File(
                Encoding.UTF8.GetBytes(json),
                "application/json",
                $"{receiptId}-{(original ? "original" : "corrected")}.json");

        var receipt = JsonSerializer.Deserialize<ReceiptData>(json, JsonOptions);
        if (receipt is null)
            return Results.Problem("Stored receipt JSON is invalid.");
        var builder = new StringBuilder("index,description,quantity,unitPrice,lineTotal\n");
        foreach (var item in receipt.Items)
        {
            builder.Append(item.Index).Append(',')
                .Append(Csv(item.Description)).Append(',')
                .Append(CsvNumber(item.Quantity)).Append(',')
                .Append(CsvNumber(item.UnitPrice)).Append(',')
                .Append(CsvNumber(item.LineTotal)).AppendLine();
        }
        return Results.File(
            Encoding.UTF8.GetBytes(builder.ToString()),
            "text/csv",
            $"{receiptId}-corrected.csv");
    }

    private static string Csv(string value)
    {
        var safe = value.TrimStart() is { Length: > 0 } trimmed &&
                   trimmed[0] is '=' or '+' or '-' or '@'
            ? "'" + value
            : value;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string CsvNumber(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "";

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

    private static object ToPublicTurn(ConversationTurnRecord turn) => new
    {
        turn.ConversationId,
        turn.IdempotencyKey,
        turn.TurnIndex,
        turn.CreatedUtc,
        turn.UserMessage,
        turn.Answer,
        events = DeserializeEvents(turn.EventsJson),
        runIds = DeserializeStrings(turn.RunIdsJson),
        attachments = DeserializeAttachments(turn.AttachmentsJson)
    };

    private static IReadOnlyList<AgentStreamEvent> DeserializeEvents(string json) =>
        Deserialize<IReadOnlyList<AgentStreamEvent>>(json) ?? [];

    private static IReadOnlyList<string> DeserializeStrings(string json) =>
        Deserialize<IReadOnlyList<string>>(json) ?? [];

    private static IReadOnlyList<ChatAttachment> DeserializeAttachments(string json) =>
        Deserialize<IReadOnlyList<ChatAttachment>>(json) ?? [];

    private static T? Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }

    private static string BuildAgentMessage(
        string message, IReadOnlyList<ChatAttachment> attachments)
    {
        if (attachments.Count == 0)
            return message;

        var references = string.Join(
            "\n",
            attachments.Select(item => $"- {item.Name ?? "image"}: `{item.Reference}`"));
        return $"{message.Trim()}\n\nAttached images available to tools:\n{references}".Trim();
    }

    private static async Task<IReadOnlyList<AgentArtifact>> DescribeArtifactsAsync(
        IImageStore store, string runId, string? scope,
        CancellationToken cancellationToken = default)
    {
        if (store is not IArtifactStore artifacts || string.IsNullOrWhiteSpace(scope))
            return [];
        var rows = await artifacts.ListAsync(scope, cancellationToken);
        return rows.Select(item => new AgentArtifact(
            runId,
            item.Name,
            item.SizeBytes,
            item.LastModifiedUtc,
            ContentTypeFor(item.Name),
            IsPreviewable(item.Name))).ToArray();
    }

    private static async Task<IReadOnlyList<AgentArtifact>> DescribeRunArtifactsAsync(
        IImageStore store, RunRecord run, CancellationToken cancellationToken)
    {
        var artifacts = await DescribeArtifactsAsync(
            store, run.RunId, run.OutputDirectory, cancellationToken);
        if (artifacts.Count > 0 ||
            string.IsNullOrWhiteSpace(run.OutputReference) ||
            !store.Exists(run.OutputReference))
            return artifacts;

        var path = store.GetImagePath(run.OutputReference);
        var file = new FileInfo(path);
        return
        [
            new AgentArtifact(
                run.RunId,
                file.Name,
                file.Exists ? file.Length : 0,
                file.Exists ? file.LastWriteTimeUtc : null,
                ContentTypeFor(path),
                IsPreviewable(path))
        ];
    }

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
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".txt" or ".log" => "text/plain",
            _ => "application/octet-stream"
        };

    private static bool IsPreviewable(string path) =>
        ContentTypeFor(path).StartsWith("image/", StringComparison.Ordinal) ||
        ContentTypeFor(path) is "application/json" or "text/csv" or "text/plain";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private sealed record SseEnvelope(string Event, object Data);

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
