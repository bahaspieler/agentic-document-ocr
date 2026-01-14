using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;

namespace UnitTests.Plugins;

public abstract class PluginTestBase
{
    protected string TempRoot = string.Empty;
    protected IImageStore Store = default!;
    protected string TestImageRef = string.Empty;

    [TestInitialize]
    public virtual void BaseSetup() { }

    [TestCleanup]
    public virtual void BaseCleanup() { }

    protected string CreateTestImage() => string.Empty;

    protected void AssertImageExists(string reference) { }

    protected void AssertImageValid(string reference) { }
}