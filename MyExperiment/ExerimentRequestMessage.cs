using MyCloudProject.Common;

namespace MyExperiment
{
    /// <summary>Queue request with the Azure identifiers required for message deletion.</summary>
    public sealed class ExerimentRequestMessage : IExerimentRequest
    {
        public string ExperimentId { get; set; } = "";
        public string InputFile { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Gets or sets the Azure Queue Storage message identifier.</summary>
        public string MessageId { get; set; } = "";

        /// <summary>Gets or sets the pop receipt required to delete the received message.</summary>
        public string MessageReceipt { get; set; } = "";
    }
}
