using Renci.SshNet;

namespace OcrAgent.Storage;

internal sealed class PooledConnection
{
    public int Id { get; init; }
    public required SftpClient Client { get; init; }
    public DateTime Created { get; init; }
    public DateTime LastUsed { get; set; }
}
