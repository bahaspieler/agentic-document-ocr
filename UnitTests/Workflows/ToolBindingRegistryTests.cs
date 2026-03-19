using Microsoft.Extensions.AI;
using OcrAgent.Workflows.Engine;

namespace UnitTests.Workflows;

[TestClass]
public class ToolBindingRegistryTests
{
    private static AIFunction CreateMockFunction(string name, Func<string> handler)
    {
        return AIFunctionFactory.Create(handler, name);
    }

    [TestMethod]
    public void Register_AndHasTool_ReturnsTrue()
    {
        var registry = new ToolBindingRegistry();
        var func = CreateMockFunction("TestTool", () => "result");

        registry.Register(func);

        Assert.IsTrue(registry.HasTool("TestTool"));
    }

    [TestMethod]
    public void HasTool_UnknownTool_ReturnsFalse()
    {
        var registry = new ToolBindingRegistry();

        Assert.IsFalse(registry.HasTool("NonExistent"));
    }

    [TestMethod]
    public void HasTool_IsCaseInsensitive()
    {
        var registry = new ToolBindingRegistry();
        var func = CreateMockFunction("MyTool", () => "ok");

        registry.Register(func);

        Assert.IsTrue(registry.HasTool("mytool"));
        Assert.IsTrue(registry.HasTool("MYTOOL"));
        Assert.IsTrue(registry.HasTool("MyTool"));
    }
}