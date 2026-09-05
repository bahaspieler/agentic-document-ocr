using System;
using System.Collections.Generic;
using System.Text;

namespace OcrAgent.Worker.Contracts
{
    /// <summary>
    /// Defines the contract for the message request that will run your experiment.
    /// </summary>
    public interface IExperimentRequest
    {
        /// <summary>
        /// Any identifier of yout choice.
        /// </summary>
        public string ExperimentId { get; set; }

        /// <summary>
        /// The URI of the file that contains the input arguments for your experiment.
        /// </summary>
        public string InputFile { get; set; }

        /// <summary>Gets or sets the natural-language experiment name or command.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets a human-readable request description.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the queue message identifier.</summary>
        public string MessageId { get; set; }

        /// <summary>Gets or sets the receipt required to update or delete the queue message.</summary>
        public string MessageReceipt { get; set; }
    }
}
