using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using MyCloudProject.Common;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyExperiment
{
    public class AzureStorageProvider : IStorageProvider, IQueueCommandStorage
    {
        private const string InlineCommandPrefix = "command:";
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly MyConfig _config;
        private readonly QueueClient _queue;
        private readonly BlobContainerClient _training;
        private readonly BlobContainerClient _results;
        private readonly TableClient _table;

        public AzureStorageProvider(IConfigurationSection configSection)
        {
            _config = new MyConfig();
            configSection.Bind(_config);
            ValidateConfiguration();

            Directory.CreateDirectory(_config.WorkRoot);
            _queue = new QueueClient(_config.StorageConnectionString, _config.Queue);
            _training = new BlobContainerClient(
                _config.StorageConnectionString, _config.TrainingContainer);
            _results = new BlobContainerClient(
                _config.StorageConnectionString, _config.ResultContainer);
            _table = new TableClient(_config.StorageConnectionString, _config.ResultTable);

            if (_config.EnsureStorageResources)
            {
                _queue.CreateIfNotExists();
                _training.CreateIfNotExists();
                _results.CreateIfNotExists();
                _table.CreateIfNotExists();
            }
        }

        public async Task CommitRequestAsync(IExerimentRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.MessageId) ||
                string.IsNullOrWhiteSpace(request.MessageReceipt))
                throw new ArgumentException(
                    "The queue message id and pop receipt are required to commit a request.",
                    nameof(request));

            await _queue.DeleteMessageAsync(
                request.MessageId, request.MessageReceipt).ConfigureAwait(false);
        }

        public async Task<string> DownloadInputAsync(string fileName)
        {
            if (fileName.StartsWith(InlineCommandPrefix, StringComparison.Ordinal))
            {
                var command = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(fileName[InlineCommandPrefix.Length..]));
                var directory = CreateDownloadDirectory();
                var path = Path.Combine(directory, "command.txt");
                await File.WriteAllTextAsync(path, command).ConfigureAwait(false);
                return path;
            }

            var blobName = ResolveBlobName(fileName);
            return await DownloadBlobAsync(blobName).ConfigureAwait(false);
        }

        public IExerimentRequest ReceiveExperimentRequestAsync(CancellationToken token)
        {
            var response = _queue.ReceiveMessages(
                maxMessages: 1,
                visibilityTimeout: TimeSpan.FromSeconds(
                    Math.Max(30, _config.MessageVisibilityTimeoutSeconds)),
                cancellationToken: token);
            QueueMessage message = response.Value.FirstOrDefault();
            if (message is null)
                return null!;

            var command = DecodeMessage(message.Body.ToString());
            if (string.IsNullOrWhiteSpace(command))
                throw new InvalidDataException("The queue command is empty.");

            return new ExerimentRequestMessage
            {
                ExperimentId = message.MessageId,
                InputFile = InlineCommandPrefix + Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(command.Trim())),
                Name = command.Trim(),
                Description = "Natural-language OCR command",
                MessageId = message.MessageId,
                MessageReceipt = message.PopReceipt
            };
        }

        public async Task<ResolvedQueueImage> ResolveImageAsync(string command)
        {
            var matches = new System.Collections.Generic.List<(string Blob, string Reference)>();
            await foreach (var blob in _training.GetBlobsAsync())
            {
                var extension = Path.GetExtension(blob.Name);
                if (!IsImageExtension(extension))
                    continue;

                var reference = Path.GetFileNameWithoutExtension(blob.Name);
                if (command.Contains(reference, StringComparison.OrdinalIgnoreCase))
                    matches.Add((blob.Name, reference));
            }

            var match = matches
                .OrderByDescending(item => item.Reference.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(match.Blob))
                throw new InvalidOperationException(
                    "The command must mention an image stored in the training container, " +
                    "for example RCPT001.");

            return new ResolvedQueueImage(
                match.Reference,
                match.Blob,
                await DownloadBlobAsync(match.Blob).ConfigureAwait(false));
        }

        public Task UploadExperimentResult(IExperimentResult result)
        {
            return UploadResultAsync("experiment", result);
        }

        public async Task UploadResultAsync(string experimentName, IExperimentResult result)
        {
            if (result is not ExperimentResult row)
                throw new ArgumentException(
                    $"Expected {nameof(ExperimentResult)}.", nameof(result));

            var uploaded = new System.Collections.Generic.List<string>();
            foreach (var localPath in row.LocalOutputFiles
                         .Where(File.Exists)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var blobName = string.Join(
                    "/",
                    SafeSegment(_config.GroupId),
                    SafeSegment(row.ExperimentId),
                    SafeFileName(localPath));
                var blob = _results.GetBlobClient(blobName);
                await blob.UploadAsync(
                    localPath,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders
                        {
                            ContentType = ContentTypeFor(localPath)
                        }
                    }).ConfigureAwait(false);
                uploaded.Add(blob.Uri.ToString());
            }

            row.OutputFiles = uploaded.ToArray();
            row.OutputFilesJson = JsonSerializer.Serialize(row.OutputFiles, JsonOptions);
            row.PrimaryOutputFileUrl = row.OutputFiles.FirstOrDefault();
            await _table.UpsertEntityAsync(row, TableUpdateMode.Replace).ConfigureAwait(false);

            foreach (var directory in row.CleanupDirectories
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                TryDeleteWorkDirectory(directory);
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_config.StorageConnectionString))
                throw new InvalidOperationException("MyConfig:StorageConnectionString is required.");
            if (string.IsNullOrWhiteSpace(_config.Queue) ||
                string.IsNullOrWhiteSpace(_config.TrainingContainer) ||
                string.IsNullOrWhiteSpace(_config.ResultContainer) ||
                string.IsNullOrWhiteSpace(_config.ResultTable) ||
                string.IsNullOrWhiteSpace(_config.GroupId))
                throw new InvalidOperationException(
                    "MyConfig queue, containers, result table, and group id are required.");
        }

        private static string DecodeMessage(string raw)
        {
            var trimmed = raw.Trim();
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(trimmed));
                return !string.IsNullOrWhiteSpace(decoded) &&
                       decoded.All(character =>
                           !char.IsControl(character) || char.IsWhiteSpace(character))
                    ? decoded
                    : trimmed;
            }
            catch (FormatException)
            {
                return trimmed;
            }
        }

        private async Task<string> DownloadBlobAsync(string blobName)
        {
            var directory = CreateDownloadDirectory();
            var localPath = Path.Combine(directory, SafeFileName(blobName));
            await _training.GetBlobClient(blobName)
                .DownloadToAsync(localPath)
                .ConfigureAwait(false);
            return localPath;
        }

        private string CreateDownloadDirectory()
        {
            var directory = Path.Combine(
                Path.GetFullPath(_config.WorkRoot), "downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private string ResolveBlobName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An input blob name is required.", nameof(value));
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return NormalizeBlobName(value);

            var segments = uri.AbsolutePath.Split(
                '/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 ||
                !segments[0].Equals(
                    _config.TrainingContainer, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Input URLs must refer to the configured '{_config.TrainingContainer}' container.");
            return NormalizeBlobName(string.Join("/", segments.Skip(1)));
        }

        private static string NormalizeBlobName(string value)
        {
            var decoded = Uri.UnescapeDataString(value).Replace('\\', '/').TrimStart('/');
            if (decoded.Split('/').Any(segment =>
                    segment is "" or "." or ".."))
                throw new InvalidOperationException("The input blob name is invalid.");
            return decoded;
        }

        private static string SafeFileName(string value)
        {
            var name = Path.GetFileName(value.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name))
                name = "input.bin";
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }

        private static string SafeSegment(string value)
        {
            var result = value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars().Concat(['/', '\\']))
                result = result.Replace(invalid, '-');
            return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
        }

        private static string ContentTypeFor(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".tif" or ".tiff" => "image/tiff",
                ".json" => "application/json",
                ".csv" => "text/csv",
                ".txt" or ".log" => "text/plain",
                _ => "application/octet-stream"
            };

        private static bool IsImageExtension(string extension) =>
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

        private void TryDeleteWorkDirectory(string path)
        {
            try
            {
                var root = Path.GetFullPath(_config.WorkRoot)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(path);
                if (candidate.StartsWith(root, StringComparison.Ordinal) &&
                    Directory.Exists(candidate))
                    Directory.Delete(candidate, recursive: true);
            }
            catch (IOException)
            {
                // Cleanup is best effort after durable upload.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best effort after durable upload.
            }
        }
    }
}
