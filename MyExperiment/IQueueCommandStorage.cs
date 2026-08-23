using System.Threading.Tasks;

namespace MyExperiment;

public interface IQueueCommandStorage
{
    Task<ResolvedQueueImage> ResolveImageAsync(string command);
}

public sealed record ResolvedQueueImage(
    string Reference,
    string BlobName,
    string LocalPath);
