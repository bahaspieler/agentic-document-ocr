using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyCloudProject.Common
{
    /// <summary>
    /// Defines the interface for experiment.
    /// </summary>
    public interface IExperiment
    {

        /// <summary>
        /// Runs the experiment.
        /// </summary>
        /// <param name="inputData">The name of the local file which contains the experiment's input data.</param>
        /// <returns>The result.</returns>
        Task<IExperimentResult> RunAsync(string inputData);

    }
}
