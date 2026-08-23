using MyCloudProject.Common;

namespace MyExperiment
{
    public sealed class ExerimentRequestMessage : IExerimentRequest
    {
        public string ExperimentId { get; set; } = "";
        public string InputFile { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string MessageReceipt { get; set; } = "";
    }
}
