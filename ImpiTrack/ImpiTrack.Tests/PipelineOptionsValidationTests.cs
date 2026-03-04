using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TcpServer;

namespace ImpiTrack.Tests;

public sealed class PipelineOptionsValidationTests
{
    [Fact]
    public void EnsureNoDeprecatedPipelineKeys_ShouldPass_WhenOnlyConsumerWorkersIsConfigured()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "2"
            })
            .Build();

        ServiceCollectionExtensions.EnsureNoDeprecatedPipelineKeys(configuration);
    }

    [Fact]
    public void EnsureNoDeprecatedPipelineKeys_ShouldThrow_WhenParserWorkersIsPresent()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TcpServerConfig:Pipeline:ParserWorkers"] = "2"
            })
            .Build();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => ServiceCollectionExtensions.EnsureNoDeprecatedPipelineKeys(configuration));

        Assert.Contains("ParserWorkers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureNoDeprecatedPipelineKeys_ShouldThrow_WhenDbWorkersIsPresent()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TcpServerConfig:Pipeline:DbWorkers"] = "2"
            })
            .Build();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => ServiceCollectionExtensions.EnsureNoDeprecatedPipelineKeys(configuration));

        Assert.Contains("DbWorkers", exception.Message, StringComparison.Ordinal);
    }
}
