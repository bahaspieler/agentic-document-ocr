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

        /// <summary>Creates the queue experiment adapter over the shared OCR engine.</summary>
        /// <param name="configSection">The MyConfig worker settings.</param>
        /// <param name="storageProvider">Storage provider used to resolve command images.</param>
        /// <param name="log">Worker logger.</param>
        public Experiment(
            IConfigurationSection configSection,
            IStorageProvider storageProvider,
            ILogger log)
        {
            _storage = storageProvider as IQueueCommandStorage
                ?? throw new ArgumentException(
                    "The queue worker requires command-aware storage.", nameof(storageProvider));
            _logger = log;
            _config = new MyConfig();
            configSection.Bind(_config);
        }

        #endregion

        #region Execution

        /// <inheritdoc />
        public async Task<IExperimentResult> RunAsync(string inputData)
        {
            var started = DateTime.UtcNow;
            var fallbackRunId = RunId.NewId();
            var commandDirectory = ParentOf(inputData);
            var command = (await File.ReadAllTextAsync(inputData).ConfigureAwait(false)).Trim();

            if (string.IsNullOrWhiteSpace(command))
            {
                _logger.LogWarning(
                    "Queue command {RunId} was empty and will be recorded as failed.",
                    fallbackRunId);
                return Failure(fallbackRunId, command, started, "The queue command is empty.",
                    [commandDirectory]);
            }

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
