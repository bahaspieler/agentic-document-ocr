using Microsoft.Extensions.AI;
using OcrAgent.Workflows.Engine;

namespace UnitTests.Workflows;

/// <summary>
/// Tests ToolBindingRegistry registration and tool lookup.
/// </summary>
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

    [TestMethod]
    public async Task InvokeAsync_CallsFunction()
    {
        var registry = new ToolBindingRegistry();
        var func = AIFunctionFactory.Create(() => "hello_world", "Greet");

        registry.Register(func);

        var result = await registry.InvokeAsync("Greet", new Dictionary<string, object?>());

        Assert.AreEqual("hello_world", result);
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownTool_ThrowsInvalidOperation()
    {
        var registry = new ToolBindingRegistry();
        bool thrown = false;

        try
        {
            await registry.InvokeAsync("Unknown", new Dictionary<string, object?>());
        }
        catch (InvalidOperationException ex)
        {
            thrown = true;
            Assert.Contains("Unknown", ex.Message);
        }

        Assert.IsTrue(thrown, "Expected InvalidOperationException for unknown tool");
    }

    [TestMethod]
    public void GetToolNames_ReturnsAllRegistered()
    {
        var registry = new ToolBindingRegistry();
        registry.Register(CreateMockFunction("ToolA", () => "a"));
        registry.Register(CreateMockFunction("ToolB", () => "b"));

        var names = registry.GetToolNames().ToList();

        Assert.HasCount(2, names);
        Assert.Contains("ToolA", names);
        Assert.Contains("ToolB", names);
    }
}
