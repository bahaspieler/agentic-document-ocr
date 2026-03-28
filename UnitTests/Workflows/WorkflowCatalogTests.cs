using OcrAgent.Workflows.Engine;

namespace UnitTests.Workflows;

[TestClass]
public class WorkflowCatalogTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"workflow_catalog_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
    private void WriteDefinition(string filename, string id, string name, string description)
    {
        var json = $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "description": "{{description}}",
          "steps": []
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, filename), json);
    }
     [TestMethod]
    public void Constructor_LoadsDefinitionsFromDirectory()
    {
        WriteDefinition("wf1.json", "wf1", "Workflow 1", "First");
        WriteDefinition("wf2.json", "wf2", "Workflow 2", "Second");

        var catalog = new WorkflowCatalog(_tempDir);

        Assert.AreEqual(2, catalog.Count);
    }
    [TestMethod]
    public void Constructor_NonExistentDirectory_CreatesEmptyCatalog()
    {
        var catalog = new WorkflowCatalog("/nonexistent/path/xyz");

        Assert.AreEqual(0, catalog.Count);
    }
     [TestMethod]
    public void Get_ExistingId_ReturnsDefinition()
    {
        WriteDefinition("test.json", "my_workflow", "My Workflow", "Test desc");

        var catalog = new WorkflowCatalog(_tempDir);
        var def = catalog.Get("my_workflow");

        Assert.AreEqual("my_workflow", def.Id);
        Assert.AreEqual("My Workflow", def.Name);
    }
    [TestMethod]
    public void Get_IsCaseInsensitive()
    {
        WriteDefinition("test.json", "MyWorkflow", "Test", "Desc");

        var catalog = new WorkflowCatalog(_tempDir);
        var def = catalog.Get("myworkflow");

        Assert.AreEqual("MyWorkflow", def.Id);
    }
    [TestMethod]
    public void Get_UnknownId_ThrowsInvalidOperation()
    {
        var catalog = new WorkflowCatalog(_tempDir);
        bool thrown = false;

        try
        {
            catalog.Get("nonexistent");
        }
        catch (InvalidOperationException ex)
        {
            thrown = true;
            Assert.IsTrue(ex.Message.Contains("nonexistent"));
        }

        Assert.IsTrue(thrown, "Expected InvalidOperationException for unknown workflow ID");
    }
}