using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OcrAgent.Worker.Contracts;
using OcrAgent.Worker.Azure;

namespace OcrAgent.Worker
{
    internal static class Program
    {
        private const string ServiceName = "Agentic Document OCR Worker";

        private static async Task Main(string[] args)
        {
            using var cancellationSource = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellationSource.Cancel();
            };

            var cfgRoot = InitHelpers.InitConfiguration(args);
            var cfgSec = cfgRoot.GetSection("WorkerOptions");
            var workerConfig = new WorkerOptions();
            cfgSec.Bind(workerConfig);
            using var logFactory = InitHelpers.InitLogging(cfgRoot);
            var logger = logFactory.CreateLogger("Train.Console");

            logger.LogInformation("Started {ServiceName}.", ServiceName);

            IStorageProvider storageProvider = new AzureStorageProvider(cfgSec);
            IExperiment experiment = new Experiment(cfgSec, storageProvider, logger);

            try
            {
                while (!cancellationSource.Token.IsCancellationRequested)
                {
                    var request =
                        storageProvider.ReceiveExperimentRequestAsync(cancellationSource.Token);

                    if (request != null)
                    {
                        try
                        {
                            var localFileWithInputArgs =
                                await storageProvider.DownloadInputAsync(request.InputFile);
                            var result = await experiment.RunAsync(localFileWithInputArgs);
                            await storageProvider.UploadResultAsync("outputfile", result);
                            await storageProvider.CommitRequestAsync(request);
                        }
                        catch (OperationCanceledException)
                            when (cancellationSource.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (request is ExperimentRequestMessage queueRequest &&
                                queueRequest.DequeueCount >=
                                Math.Max(1, workerConfig.MaximumDequeueAttempts) &&
                                storageProvider is IQueueCommandStorage queueStorage)
                            {
                                await queueStorage.MoveToPoisonAsync(
                                    queueRequest, ex.ToString());
                                logger.LogError(
                                    ex,
                                    "Queue experiment moved to poison queue after {AttemptCount} attempts.",
                                    queueRequest.DequeueCount);
                            }
                            else
                            {
                                // The message stays uncommitted and becomes visible for a Queue retry.
                                logger.LogError(ex, "Queue experiment failed.");
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(500, cancellationSource.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
                // Ctrl+C requests an orderly worker shutdown.
            }

            logger.LogInformation("{ServiceName} stopped.", ServiceName);
        }
    }
}
