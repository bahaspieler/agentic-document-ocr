using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;

namespace UnitTests.Storage;

/// <summary>Tests storage-provider selection and invalid provider handling.</summary>
[TestClass]
public sealed class StorageFactoryTests
{
    [TestMethod]
    public void Create_WithLocalProvider_ReturnsLocalFileImageStore()
    {
        var config = new StorageConfiguration
        {
            Provider = "local",
            Local = new LocalStorageConfig { RootPath = Path.GetTempPath() },
        };

        var store = StorageFactory.Create(config);

        Assert.IsInstanceOfType<LocalFileImageStore>(store);
    }

    [TestMethod]
    public void Create_WithSftpProvider_ReturnsSftpImageStore()
    {
        var config = new StorageConfiguration
        {
            Provider = "sftp",
            Sftp = new SftpStorageConfig
            {
                Host = "localhost",
                Port = 2222,
                Username = "test",
                Password = "test",
                RemoteBasePath = "/test",
            },
        };

        var store = StorageFactory.Create(config);

        Assert.IsInstanceOfType<SftpImageStore>(store);
        (store as IDisposable)?.Dispose();
    }

    [TestMethod]
    public void Create_WithUnknownProvider_ThrowsArgumentException()
    {
        var config = new StorageConfiguration { Provider = "unknown" };

        Assert.ThrowsExactly<ArgumentException>(() => StorageFactory.Create(config));
    }

    [TestMethod]
    public void Create_WithNullConfig_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => StorageFactory.Create(null!));
    }
}
