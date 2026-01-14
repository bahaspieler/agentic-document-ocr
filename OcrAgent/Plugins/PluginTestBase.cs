using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Base class for plugin tests with common test utilities.
/// </summary>
public abstract class PluginTestBase
{
    protected string TempRoot = null!;
    protected LocalFileImageStore Store = null!;
    protected string TestImageRef = null!;
}
