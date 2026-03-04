using ImpiTrack.Tcp.Core.Configuration;
using TcpServer.Configuration;

namespace ImpiTrack.Tests;

public sealed class ConsumerWorkerResolverTests
{
    [Fact]
    public void Resolve_ShouldPreferConsumerWorkers_WhenKeyIsPresent()
    {
        var pipeline = new TcpPipelineOptions
        {
            ConsumerWorkers = 4,
            ParserWorkers = 9,
            DbWorkers = 11
        };

        ConsumerWorkerResolution result = ConsumerWorkerResolver.Resolve(
            pipeline,
            hasConsumerWorkersKey: true,
            hasParserWorkersKey: true,
            hasDbWorkersKey: true);

        Assert.Equal(4, result.WorkerCount);
        Assert.True(result.HasDeprecatedKeys);
        Assert.False(result.UsedDeprecatedFallback);
        Assert.Equal(nameof(TcpPipelineOptions.ConsumerWorkers), result.ResolvedFrom);
    }

    [Fact]
    public void Resolve_ShouldFallbackToDeprecatedKeys_WhenConsumerWorkersKeyIsMissing()
    {
        var pipeline = new TcpPipelineOptions
        {
            ConsumerWorkers = 2,
            ParserWorkers = 3,
            DbWorkers = 5
        };

        ConsumerWorkerResolution result = ConsumerWorkerResolver.Resolve(
            pipeline,
            hasConsumerWorkersKey: false,
            hasParserWorkersKey: true,
            hasDbWorkersKey: true);

        Assert.Equal(5, result.WorkerCount);
        Assert.True(result.HasDeprecatedKeys);
        Assert.True(result.UsedDeprecatedFallback);
        Assert.Equal($"{nameof(TcpPipelineOptions.ParserWorkers)}|{nameof(TcpPipelineOptions.DbWorkers)}", result.ResolvedFrom);
    }

    [Fact]
    public void Resolve_ShouldUseConsumerWorkers_WhenDeprecatedKeysAreNotPresent()
    {
        var pipeline = new TcpPipelineOptions
        {
            ConsumerWorkers = 1,
            ParserWorkers = 8,
            DbWorkers = 8
        };

        ConsumerWorkerResolution result = ConsumerWorkerResolver.Resolve(
            pipeline,
            hasConsumerWorkersKey: false,
            hasParserWorkersKey: false,
            hasDbWorkersKey: false);

        Assert.Equal(1, result.WorkerCount);
        Assert.False(result.HasDeprecatedKeys);
        Assert.False(result.UsedDeprecatedFallback);
        Assert.Equal(nameof(TcpPipelineOptions.ConsumerWorkers), result.ResolvedFrom);
    }
}
