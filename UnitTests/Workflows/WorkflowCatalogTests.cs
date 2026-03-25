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
}