using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyCloudProject.Common;
using MyExperiment;
using OcrAgent.Agent;

namespace UnitTests.Cloud;

/// <summary>Tests parent fan-out and retry behavior without Azure or a model call.</summary>
[TestClass]
public sealed class BulkDispatchTests
{
    /// <summary>Persists one plan before fan-out and reuses it on parent redelivery.</summary>
    [TestMethod]
    public async Task RunAsync_RedeliveredParent_ReusesPlanAndStableItemIds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MyConfig:GroupId"] = "HiddenLayer",
                ["MyConfig:MaximumBatchImages"] = "10"
            })
            .Build();
        var storage = new BulkDispatchStorage();
        var plannerCalls = 0;
        var experiment = new Experiment(
            configuration.GetSection("MyConfig"),
            storage,
            NullLogger.Instance,
            _ =>
            {
                plannerCalls++;
                return Task.FromResult(new BulkActionPlan(
                    [new BulkPlanStep("tool", "DenoiseImage")]));
            });
        var input = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            input,
            JsonSerializer.Serialize(new QueueCommandInput
            {
                RequestId = "parent-message-1",
                Command = "Denoise all images in cord-smoke."
            }));

        try
        {
            var first = (ExperimentResult)await experiment.RunAsync(input);
            var firstIds = storage.Messages.Select(item => item.ItemId).ToArray();
            storage.Messages.Clear();
            var second = (ExperimentResult)await experiment.RunAsync(input);
            var secondIds = storage.Messages.Select(item => item.ItemId).ToArray();

            Assert.AreEqual(1, plannerCalls);
            Assert.AreEqual("dispatched", first.Status);
            Assert.AreEqual(first.ExperimentId, second.ExperimentId);
            CollectionAssert.AreEqual(firstIds, secondIds);
            Assert.AreEqual(2, firstIds.Distinct().Count());
            Assert.IsTrue(storage.PlanWasSavedBeforeEveryEnqueue);
        }
        finally
        {
            File.Delete(input);
        }
    }

}
