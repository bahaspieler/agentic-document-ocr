using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyCloudProject.Common;
using OcrAgent.Agent;
using OcrAgent.Execution;
using OcrAgent.Hosting;
using OcrAgent.Runs;
using OcrAgent.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyExperiment
{
    /// <summary>Runs one natural-language queue command through the existing OCR agent.</summary>
    public class Experiment : IExperiment
    {
        #region Construction

        private readonly IQueueCommandStorage _storage;
        private readonly ILogger _logger;
        private readonly MyConfig _config;
        private readonly Func<string, Task<BulkActionPlan>>? _bulkPlanner;

        /// <summary>Creates the queue experiment adapter over the shared OCR engine.</summary>
        /// <param name="configSection">The MyConfig worker settings.</param>
        /// <param name="storageProvider">Storage provider used to resolve command images.</param>
        /// <param name="log">Worker logger.</param>
        public Experiment(
            IConfigurationSection configSection,
            IStorageProvider storageProvider,
            ILogger log)
            : this(configSection, storageProvider, log, null)
        {
        }

        /// <summary>Creates the adapter with an injectable planner for deterministic host tests.</summary>
        /// <param name="configSection">The MyConfig worker settings.</param>
        /// <param name="storageProvider">Storage provider used to resolve command images.</param>
        /// <param name="log">Worker logger.</param>
        /// <param name="bulkPlanner">Optional one-time planner override.</param>
        public Experiment(
            IConfigurationSection configSection,
            IStorageProvider storageProvider,
            ILogger log,
            Func<string, Task<BulkActionPlan>>? bulkPlanner)
        {
            _storage = storageProvider as IQueueCommandStorage
                ?? throw new ArgumentException(
                    "The queue worker requires command-aware storage.", nameof(storageProvider));
            _logger = log;
            _config = new MyConfig();
            configSection.Bind(_config);
            _bulkPlanner = bulkPlanner;
        }

        #endregion

        #region Execution

        /// <inheritdoc />
        public async Task<IExperimentResult> RunAsync(string inputData)
        {
            var started = DateTime.UtcNow;
            var commandDirectory = ParentOf(inputData);
            QueueCommandInput input;
            var inputText = await File.ReadAllTextAsync(inputData).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(inputText))
            {
                input = new QueueCommandInput();
            }
            else if (!inputText.TrimStart().StartsWith('{'))
            {
                input = new QueueCommandInput
                {
                    RequestId = RunId.NewId(),
                    Command = inputText.Trim()
                };
            }
            else
            try
            {
                input = JsonSerializer.Deserialize<QueueCommandInput>(
                    inputText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new JsonException("Worker input was empty.");
            }
            catch (JsonException ex)
            {
                return Failure(
                    RunId.NewId(), "", started,
                    $"The queue input contract is invalid: {ex.Message}",
                    [commandDirectory]);
            }

            if (string.IsNullOrWhiteSpace(input.Command))
            {
                var runId = string.IsNullOrWhiteSpace(input.RequestId)
                    ? RunId.NewId()
                    : input.RequestId;
                _logger.LogWarning(
                    "Queue command {RunId} was empty and will be recorded as failed.",
                    runId);
                return Failure(runId, input.Command, started, "The queue command is empty.",
                    [commandDirectory]);
            }

            if (input.Kind.Equals("bulk-item", StringComparison.OrdinalIgnoreCase))
                return await RunBulkItemAsync(input, started, commandDirectory)
                    .ConfigureAwait(false);
            if (BulkCommandParser.IsBulkCommand(input.Command))
                return await DispatchBulkAsync(input, started, commandDirectory)
                    .ConfigureAwait(false);

            return await RunSingleAsync(input, started, commandDirectory)
                .ConfigureAwait(false);
        }

        private async Task<ExperimentResult> RunSingleAsync(
            QueueCommandInput input,
            DateTime started,
            string commandDirectory)
        {
            var command = input.Command;
            var fallbackRunId = string.IsNullOrWhiteSpace(input.RequestId)
                ? RunId.NewId()
                : input.RequestId;
            _logger.LogInformation(
                "Processing queue command {RunId} with {CommandLength} characters.",
                fallbackRunId,
                command.Length);

            ResolvedQueueImage image;
            try
            {
                image = await _storage.ResolveImageAsync(command).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not resolve an image for queue command {RunId}.",
                    fallbackRunId);
                return Failure(fallbackRunId, command, started, ex.Message, [commandDirectory]);
            }

            var cleanup = new List<string> { commandDirectory, ParentOf(image.LocalPath) };
            using var host = EngineHost.Create("queue");
            host.Store.SetActiveSubdirectory($"queue/{fallbackRunId}");

            try
            {
                var imageReference = host.Store.SaveFromFile(image.LocalPath, image.Reference);
                var agent = AgentService.Create(host.Aggregator.GetAllTools());
                var execution = await new ExecutionCore(host, agent).ExecuteAsync(
                    new ExecutionRequest(
                        ExecutionMode.Agent,
                        Message:
                            $"{command}\n\nUse the exact image reference `{imageReference}` " +
                            "for every tool or workflow call.",
                        Source: "queue")).ConfigureAwait(false);

                var runId = execution.PrimaryRun?.RunId ?? fallbackRunId;
                var outputDirectory = Path.Combine(
                    Path.GetFullPath(_config.WorkRoot), "outputs", runId);
                cleanup.Add(outputDirectory);
                var files = await CollectArtifactsAsync(
                    host.Store, execution, outputDirectory).ConfigureAwait(false);

                var ended = DateTime.UtcNow;
                var success = execution.Runs.All(run => run.Success) &&
                              !string.IsNullOrWhiteSpace(execution.Output);
                var error = execution.Runs.FirstOrDefault(run => !run.Success)?.Error;

                _logger.LogInformation(
                    "Queue command completed as run {RunId}; success={Success}, artifacts={ArtifactCount}.",
                    runId, success, files.Count);

                return new ExperimentResult(_config.GroupId, runId)
                {
                    ExperimentId = runId,
                    Name = command,
                    Description = "Natural-language queue command",
                    Mode = "agent",
                    Status = success ? "succeeded" : "failed",
                    Success = success,
                    Error = error,
                    Answer = execution.Output,
                    PrimaryRunId = execution.PrimaryRun?.RunId,
                    InputFileUrl = image.BlobName,
                    StartTimeUtc = started,
                    EndTimeUtc = ended,
                    Duration = ended - started,
                    DurationSec = Math.Max(0, (long)Math.Ceiling((ended - started).TotalSeconds)),
                    LocalOutputFiles = files.ToArray(),
                    CleanupDirectories = cleanup.ToArray()
                };
            }
            // Expected command and tool errors become durable failed result rows.
            // Other failures propagate so that the queue message remains uncommitted.
            catch (Exception ex) when (
                ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                _logger.LogError(ex, "Queue command {RunId} failed.", fallbackRunId);
                return Failure(
                    fallbackRunId, command, started, ex.Message, cleanup.ToArray(), image.BlobName);
            }
            finally
            {
                // Do not leak the queue run scope into a later command.
                host.Store.SetActiveSubdirectory(null);
            }
        }

        private async Task<ExperimentResult> DispatchBulkAsync(
            QueueCommandInput input,
            DateTime started,
            string commandDirectory)
        {
            var batchId = StableId("batch", input.RequestId, input.Command);
            var (datasetName, images) = await _storage
                .ListDatasetImagesAsync(input.Command, _config.MaximumBatchImages)
                .ConfigureAwait(false);
            var existing = await _storage.GetResultAsync(batchId).ConfigureAwait(false);
            BulkActionPlan plan;
            if (!string.IsNullOrWhiteSpace(existing?.PlanJson))
            {
                plan = DeserializePlan(existing.PlanJson);
            }
            else
            {
                if (_bulkPlanner is not null)
                {
                    plan = await _bulkPlanner(input.Command).ConfigureAwait(false);
                }
                else
                {
                    using var host = EngineHost.Create("queue-bulk-planner");
                    plan = await new BulkActionPlanner(host.Aggregator)
                        .PlanAsync(input.Command).ConfigureAwait(false);
                }
            }

            var planJson = JsonSerializer.Serialize(plan);
            var parent = existing ?? new ExperimentResult(_config.GroupId, batchId)
            {
                ExperimentId = batchId,
                ResultKind = "batch",
                BatchId = batchId,
                Name = input.Command,
                Description = "Natural-language bulk queue command",
                Mode = "bulk-plan",
                StartTimeUtc = started
            };
            parent.DatasetName = datasetName;
            parent.PlanJson = planJson;
            parent.ItemCount = images.Count;
            parent.Status = "dispatching";
            parent.Success = false;
            parent.EndTimeUtc = null;
            await _storage.SaveBatchPlanAsync(parent).ConfigureAwait(false);

            var messages = images.Select(image => new BulkQueueMessage
            {
                BatchId = batchId,
                ItemId = StableId("item", batchId, image.BlobName),
                Command = input.Command,
                BlobName = image.BlobName,
                PlanJson = planJson
            });
            await _storage.EnqueueBulkItemsAsync(messages).ConfigureAwait(false);

            var ended = DateTime.UtcNow;
            parent.Status = "dispatched";
            parent.Success = true;
            parent.Answer =
                $"Dispatched {images.Count} images from images/{datasetName}/.";
            parent.EndTimeUtc = ended;
            parent.Duration = ended - started;
            parent.DurationSec = Math.Max(
                0, (long)Math.Ceiling((ended - started).TotalSeconds));
            parent.CleanupDirectories = [commandDirectory];
            _logger.LogInformation(
                "Bulk batch {BatchId} dispatched {ItemCount} items for dataset {DatasetName}.",
                batchId, images.Count, datasetName);
            return parent;
        }

        private async Task<ExperimentResult> RunBulkItemAsync(
            QueueCommandInput input,
            DateTime started,
            string commandDirectory)
        {
            if (string.IsNullOrWhiteSpace(input.BatchId) ||
                string.IsNullOrWhiteSpace(input.ItemId) ||
                string.IsNullOrWhiteSpace(input.BlobName) ||
                string.IsNullOrWhiteSpace(input.PlanJson))
                return Failure(
                    string.IsNullOrWhiteSpace(input.ItemId) ? RunId.NewId() : input.ItemId,
                    input.Command,
                    started,
                    "The internal bulk-item message is incomplete.",
                    [commandDirectory],
                    input.BlobName);

            var persisted = await _storage.GetResultAsync(input.ItemId).ConfigureAwait(false);
            if (persisted is not null)
            {
                persisted.CleanupDirectories = [commandDirectory];
                return persisted;
            }

            var image = await _storage.ResolveImageAsync(input.Command, input.BlobName)
                .ConfigureAwait(false);
            var cleanup = new List<string> { commandDirectory, ParentOf(image.LocalPath) };
            using var host = EngineHost.Create("queue-bulk-item");
            host.Store.SetActiveSubdirectory($"queue/{input.ItemId}");
            try
            {
                var plan = BulkActionPlanner.Validate(
                    DeserializePlan(input.PlanJson),
                    host.Aggregator.GetAllTools()
                        .OfType<Microsoft.Extensions.AI.AIFunction>()
                        .Select(tool => tool.Name),
                    host.Aggregator.Workflows.ListWorkflows()
                        .Split('\n')
                        .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
                        .Select(line => line[2..].Split(':', 2)[0].Trim()));
                var imageReference = host.Store.SaveFromFile(
                    image.LocalPath, image.Reference);
                var core = new ExecutionCore(host);
                var outputs = new List<string>();
                var outputDirectory = Path.Combine(
                    Path.GetFullPath(_config.WorkRoot), "outputs", input.ItemId);
                cleanup.Add(outputDirectory);
                ExecutionResult? final = null;
                foreach (var step in plan.Steps)
                {
                    final = await core.ExecuteAsync(new ExecutionRequest(
                        step.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase)
                            ? ExecutionMode.Workflow
                            : ExecutionMode.Tool,
                        Name: step.Name,
                        ImageReference: imageReference,
                        OverridesJson:
                            step.Mode.Equals(
                                "workflow", StringComparison.OrdinalIgnoreCase) &&
                            step.Arguments is not null
                                ? JsonSerializer.Serialize(step.Arguments)
                                : null,
                        Arguments:
                            step.Mode.Equals(
                                "tool", StringComparison.OrdinalIgnoreCase)
                                ? step.Arguments
                                : null,
                        Source: "queue-bulk-item")).ConfigureAwait(false);
                    outputs.AddRange(await CollectArtifactsAsync(
                        host.Store, final, outputDirectory).ConfigureAwait(false));
                    imageReference = final.Output;
                }

                var ended = DateTime.UtcNow;
                var success = final is not null &&
                              final.Runs.All(run => run.Success) &&
                              !string.IsNullOrWhiteSpace(final.Output);
                return new ExperimentResult(_config.GroupId, input.ItemId)
                {
                    ExperimentId = input.ItemId,
                    ResultKind = "batch-item",
                    BatchId = input.BatchId,
                    BatchItemId = input.ItemId,
                    SourceBlobName = input.BlobName,
                    PlanJson = input.PlanJson,
                    Name = input.Command,
                    Description = "Deterministic bulk queue item",
                    Mode = "bulk-plan",
                    Status = success ? "succeeded" : "failed",
                    Success = success,
                    Error = final?.Runs.FirstOrDefault(run => !run.Success)?.Error,
                    Answer = final?.Output,
                    PrimaryRunId = final?.PrimaryRun?.RunId,
                    InputFileUrl = input.BlobName,
                    StartTimeUtc = started,
                    EndTimeUtc = ended,
                    Duration = ended - started,
                    DurationSec = Math.Max(
                        0, (long)Math.Ceiling((ended - started).TotalSeconds)),
                    LocalOutputFiles = outputs
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    CleanupDirectories = cleanup.ToArray()
                };
            }
            catch (Exception ex) when (
                ex is ArgumentException or InvalidOperationException or
                KeyNotFoundException or JsonException)
            {
                var failure = Failure(
                    input.ItemId,
                    input.Command,
                    started,
                    ex.Message,
                    cleanup.ToArray(),
                    input.BlobName);
                failure.ResultKind = "batch-item";
                failure.BatchId = input.BatchId;
                failure.BatchItemId = input.ItemId;
                failure.SourceBlobName = input.BlobName;
                failure.PlanJson = input.PlanJson;
                return failure;
            }
            finally
            {
                host.Store.SetActiveSubdirectory(null);
            }
        }

        #endregion

        #region Artifact Collection

        private async Task<List<string>> CollectArtifactsAsync(
            IImageStore store,
            ExecutionResult execution,
            string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var files = new List<string>();

            foreach (var artifact in execution.Artifacts)
            {
                var run = execution.Runs.FirstOrDefault(item => item.RunId == artifact.RunId);
                if (run is null)
                    continue;
                // A standalone tool exports its primary output, not intermediate debug files.
                if (run.Mode.Equals("tool", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(run.OutputReference) &&
                    !Path.GetFileNameWithoutExtension(artifact.Name).Equals(
                        Path.GetFileName(run.OutputReference),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = UniquePath(outputDirectory, SafeFileName(artifact.Name));
                if (!string.IsNullOrWhiteSpace(run.OutputDirectory) &&
                    store is IArtifactStore artifactStore)
                {
                    await using var source = await artifactStore
                        .OpenReadAsync(run.OutputDirectory, artifact.Name)
                        .ConfigureAwait(false);
                    await using var target = File.Create(path);
                    await source.CopyToAsync(target).ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(run.OutputReference) &&
                         store.Exists(run.OutputReference))
                {
                    File.Copy(store.GetImagePath(run.OutputReference), path);
                }
                else
                {
                    continue;
                }
                files.Add(path);
            }

            return files;
        }

        #endregion

        #region Result and Path Helpers

        private ExperimentResult Failure(
            string runId,
            string command,
            DateTime started,
            string error,
            string[] cleanup,
            string imageBlob = "")
        {
            var ended = DateTime.UtcNow;
            return new ExperimentResult(_config.GroupId, runId)
            {
                ExperimentId = runId,
                Name = command,
                Description = "Natural-language queue command",
                Mode = "agent",
                Status = "failed",
                Success = false,
                Error = error,
                InputFileUrl = imageBlob,
                StartTimeUtc = started,
                EndTimeUtc = ended,
                Duration = ended - started,
                DurationSec = Math.Max(0, (long)Math.Ceiling((ended - started).TotalSeconds)),
                CleanupDirectories = cleanup
            };
        }

        private static string ParentOf(string path) =>
            Path.GetDirectoryName(Path.GetFullPath(path)) ?? Path.GetFullPath(".");

        private static BulkActionPlan DeserializePlan(string json) =>
            JsonSerializer.Deserialize<BulkActionPlan>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("The bulk plan is empty.");

        private static string StableId(
            string prefix,
            string first,
            string second)
        {
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes($"{first}\n{second}"));
            return $"{prefix}-{Convert.ToHexString(bytes)[..32].ToLowerInvariant()}";
        }

        private static string SafeFileName(string value)
        {
            var name = Path.GetFileName(value.Replace('\\', '/'));
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(name) ? "artifact.bin" : name;
        }

        private static string UniquePath(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            return !File.Exists(path)
                ? path
                : Path.Combine(
                    directory,
                    $"{Path.GetFileNameWithoutExtension(name)}-{Guid.NewGuid():N}" +
                    Path.GetExtension(name));
        }

        #endregion
    }
}
