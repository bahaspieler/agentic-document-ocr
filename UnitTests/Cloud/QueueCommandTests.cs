using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OcrAgent.Worker.Contracts;
using OcrAgent.Worker.Azure;

namespace UnitTests.Cloud;

[TestClass]
public sealed class QueueCommandTests
{
    [TestMethod]
    [DataRow(nameof(ExperimentResult.OutputFiles))]
    [DataRow(nameof(ExperimentResult.LocalOutputFiles))]
    [DataRow(nameof(ExperimentResult.CleanupDirectories))]
    [DataRow(nameof(ExperimentResult.Duration))]
    public void RuntimeOnlyResultProperties_AreNotTableColumns(string propertyName)
    {
        var property = typeof(ExperimentResult).GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public);

        Assert.IsNotNull(property);
        Assert.IsNotNull(property.GetCustomAttribute<IgnoreDataMemberAttribute>());
    }

    [TestMethod]
    public async Task EmptyQueueCommand_ProducesHandledFailure()
    {
        var path = Path.GetTempFileName();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WorkerOptions:GroupId"] = "AgenticDocumentOcr"
                })
                .Build();
            var experiment = new Experiment(
                configuration.GetSection("WorkerOptions"),
                new UnusedCommandStorage(),
                NullLogger.Instance);

            var untypedResult = await experiment.RunAsync(path);
            var result = untypedResult as ExperimentResult;

            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("failed", result.Status);
            StringAssert.Contains(result.Error, "empty");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
