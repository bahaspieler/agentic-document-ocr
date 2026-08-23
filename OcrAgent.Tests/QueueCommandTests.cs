using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyCloudProject.Common;
using MyExperiment;

namespace OcrAgent.Tests;

public sealed class QueueCommandTests
{
    [Theory]
    [InlineData(nameof(ExperimentResult.OutputFiles))]
    [InlineData(nameof(ExperimentResult.LocalOutputFiles))]
    [InlineData(nameof(ExperimentResult.CleanupDirectories))]
    [InlineData(nameof(ExperimentResult.Duration))]
    public void RuntimeOnlyResultPropertiesAreNotTableColumns(string propertyName)
    {
        var property = typeof(ExperimentResult).GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<IgnoreDataMemberAttribute>());
    }

    [Fact]
    public async Task EmptyQueueCommandProducesHandledFailure()
    {
        var path = Path.GetTempFileName();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MyConfig:GroupId"] = "HiddenLayer"
                })
                .Build();
            var experiment = new Experiment(
                configuration.GetSection("MyConfig"),
                new UnusedCommandStorage(),
                NullLogger.Instance);

            var result = Assert.IsType<ExperimentResult>(
                await experiment.RunAsync(path));

            Assert.False(result.Success);
            Assert.Equal("failed", result.Status);
            Assert.Contains("empty", result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class UnusedCommandStorage : IStorageProvider, IQueueCommandStorage
    {
        public Task<ResolvedQueueImage> ResolveImageAsync(string command) =>
            throw new Xunit.Sdk.XunitException(
                "An empty command must fail before image resolution.");

        public IExerimentRequest ReceiveExperimentRequestAsync(CancellationToken token) =>
            throw new NotSupportedException();

        public Task<string> DownloadInputAsync(string fileName) =>
            throw new NotSupportedException();

        public Task UploadResultAsync(string experimentName, IExperimentResult result) =>
            throw new NotSupportedException();

        public Task CommitRequestAsync(IExerimentRequest request) =>
            throw new NotSupportedException();
    }
}
