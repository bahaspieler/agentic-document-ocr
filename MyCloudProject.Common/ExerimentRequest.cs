using System;
using System.Collections.Generic;
using System.Text;

namespace MyCloudProject.Common
{
    /// <summary>
    /// Defines the contract for the message request that will run your experiment.
    /// </summary>
    public interface IExerimentRequest
    {
        /// <summary>
        /// Any identifier of yout choice.
        /// </summary>
        public string ExperimentId { get; set; }

        /// <summary>
        /// The URI of the file that contains the input arguments for your experiment.
        /// </summary>
        public string InputFile { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string MessageId { get; set; }

        public string MessageReceipt { get; set; }
    }
}
