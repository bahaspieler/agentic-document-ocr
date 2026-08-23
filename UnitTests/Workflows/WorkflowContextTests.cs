using OcrAgent.Workflows.Engine;

namespace UnitTests.Workflows;

[TestClass]
public class WorkflowContextTests
{
    [TestMethod]
    public void Set_And_Get_ReturnsStoredValue()
    {
        var ctx = new WorkflowContext();
        ctx.Set("key1", "value1");
        Assert.AreEqual("value1", ctx.Get("key1"));
    }

    [TestMethod]
    public void Get_MissingKey_ReturnsNull()
    {
        var ctx = new WorkflowContext();
        Assert.IsNull(ctx.Get("nonexistent"));
    }

    [TestMethod]
    public void Has_ExistingKey_ReturnsTrue()
    {
        var ctx = new WorkflowContext();
        ctx.Set("present", "data");
        Assert.IsTrue(ctx.Has("present"));
    }

    [TestMethod]
    public void Has_MissingKey_ReturnsFalse()
    {
        var ctx = new WorkflowContext();
        Assert.IsFalse(ctx.Has("missing"));
    }

    [TestMethod]
    public void Set_OverwritesExistingValue()
    {
        var ctx = new WorkflowContext();
        ctx.Set("key", "old");
        ctx.Set("key", "new");
        Assert.AreEqual("new", ctx.Get("key"));
    }

    [TestMethod]
    public void GetAll_ReturnsAllEntries()
    {
        var ctx = new WorkflowContext();
        ctx.Set("a", "1");
        ctx.Set("b", "2");
        var all = ctx.GetAll();
        Assert.HasCount(2, all);
        Assert.AreEqual("1", all["a"]);
        Assert.AreEqual("2", all["b"]);
    }

    [TestMethod]
    public void GetAll_ReturnsReadOnlySnapshot()
    {
        var ctx = new WorkflowContext();
        ctx.Set("k", "v");
        var snapshot = ctx.GetAll();
        Assert.IsTrue(snapshot is IReadOnlyDictionary<string, string>);
    }
}
