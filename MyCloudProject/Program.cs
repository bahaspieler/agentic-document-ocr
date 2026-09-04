using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyCloudProject.Common;
using MyExperiment;

namespace MyCloudProject
{
    internal static class Program
    {
        private const string ProjectName = "ML 25/26-01";

        private static async Task Main(string[] args)
        {
            using var cancellationSource = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellationSource.Cancel();
            };

            var cfgRoot = Common.InitHelpers.InitConfiguration(args);
            var cfgSec = cfgRoot.GetSection("MyConfig");
            var workerConfig = new MyConfig();
            cfgSec.Bind(workerConfig);
            using var logFactory = InitHelpers.InitLogging(cfgRoot);
            var logger = logFactory.CreateLogger("Train.Console");

            logger.LogInformation("Started experiment {ProjectName}.", ProjectName);

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
                            if (request is ExerimentRequestMessage queueRequest &&
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

            logger.LogInformation("Experiment {ProjectName} stopped.", ProjectName);
        }
    }
}
