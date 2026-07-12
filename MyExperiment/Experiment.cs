using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyCloudProject.Common;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyExperiment
{
    /// <summary>
    /// This class implements the ML experiment that will run in the cloud. This is refactored code from my SE project.
    /// </summary>
    public class Experiment : IExperiment
    {
        private IStorageProvider storageProvider;

        private ILogger logger;

        private MyConfig config;

        public Experiment(IConfigurationSection configSection, IStorageProvider storageProvider, ILogger log)
        {
            this.storageProvider = storageProvider;
            this.logger = log;

            config = new MyConfig();
            configSection.Bind(config);
        }


        public Task<IExperimentResult> RunAsync(string inputData)
        {
            // TODO read file

            // YOU START HERE WITH YOUR SE EXPERIMENT!!!!

            ExperimentResult res = new ExperimentResult(this.config.GroupId, null);

            // logging, logging and logging

            res.StartTimeUtc = DateTime.UtcNow;

            // Run your experiment code here.

            return Task.FromResult<IExperimentResult>(res); // TODO...
        }
    }
}
