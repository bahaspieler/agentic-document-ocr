using OcrAgent.Worker.Contracts;

namespace OcrAgent.Worker.Azure
{
    /// <summary>Queue request with the Azure identifiers required for message deletion.</summary>
    public sealed class ExperimentRequestMessage : IExperimentRequest
    {
        /// <summary>Gets or sets the experiment identifier assigned from the queue message.</summary>
        public string ExperimentId { get; set; } = "";

        /// <summary>Gets or sets the encoded command consumed by the experiment.</summary>
        public string InputFile { get; set; } = "";

        /// <summary>Gets or sets the natural-language OCR command.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets the request description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Gets or sets the Azure Queue Storage message identifier.</summary>
        public string MessageId { get; set; } = "";

        /// <summary>Gets or sets the pop receipt required to delete the received message.</summary>
        public string MessageReceipt { get; set; } = "";

        /// <summary>Gets or sets the dequeue-attempt count reported by Azure Queue Storage.</summary>
        public long DequeueCount { get; set; }

        /// <summary>Gets or sets the internal message contract kind.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Gets or sets the parent batch identifier.</summary>
        public string BatchId { get; set; } = "";

        /// <summary>Gets or sets the deterministic batch-item identifier.</summary>
        public string ItemId { get; set; } = "";

        /// <summary>Gets or sets the exact source Blob name.</summary>
        public string BlobName { get; set; } = "";

        /// <summary>Gets or sets the validated serialized item plan.</summary>
        public string PlanJson { get; set; } = "";
    }
}
