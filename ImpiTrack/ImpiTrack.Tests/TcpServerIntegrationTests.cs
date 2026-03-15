using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using TcpServer;

namespace ImpiTrack.Tests;

public sealed class TcpServerIntegrationTests
{
    [Fact]
    public async Task TcpServer_ShouldAckCobanLoginAndTrackerMessages()
    {
        int port = GetAvailablePort();
        var data = new Dictionary<string, string?>
        {
            ["TcpServerConfig:Servers:0:Name"] = "TestCoban",
            ["TcpServerConfig:Servers:0:Port"] = port.ToString(),
            ["TcpServerConfig:Servers:0:Protocol"] = "COBAN",
            ["TcpServerConfig:Socket:ReceiveBufferBytes"] = "4096",
            ["TcpServerConfig:Socket:MaxFrameBytes"] = "16384",
            ["TcpServerConfig:Socket:ReadTimeoutSeconds"] = "30",
            ["TcpServerConfig:Socket:IdleTimeoutSeconds"] = "180",
            ["TcpServerConfig:Socket:HandshakeTimeoutSeconds"] = "15",
            ["TcpServerConfig:Pipeline:ChannelCapacity"] = "2000",
            ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "1",
            ["EventBus:Provider"] = "InMemory"
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(data);
        builder.Services.AddTcpServerServices(builder.Configuration);
        using IHost host = builder.Build();
        await host.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream stream = client.GetStream();

        await SendAsync(stream, "##,imei:359586015829802,A;");
        string loginAck = await ReadAckAsync(stream);
        Assert.Equal("LOAD", loginAck);

        await SendAsync(stream, "imei:359586015829802,tracker,250208125816,,F,175816.000,A,0228.81052,N,07634.01441,W,,;");
        string trackAck = await ReadAckAsync(stream);
        Assert.Equal("ON\r\n", trackAck);

        await host.StopAsync();
    }

    [Fact]
    public async Task TcpServer_ShouldEchoCantrackLoginAndHeartbeat()
    {
        int port = GetAvailablePort();
        var data = new Dictionary<string, string?>
        {
            ["TcpServerConfig:Servers:0:Name"] = "TestCantrack",
            ["TcpServerConfig:Servers:0:Port"] = port.ToString(),
            ["TcpServerConfig:Servers:0:Protocol"] = "CANTRACK",
            ["TcpServerConfig:Socket:ReceiveBufferBytes"] = "4096",
            ["TcpServerConfig:Socket:MaxFrameBytes"] = "16384",
            ["TcpServerConfig:Socket:ReadTimeoutSeconds"] = "30",
            ["TcpServerConfig:Socket:IdleTimeoutSeconds"] = "180",
            ["TcpServerConfig:Socket:HandshakeTimeoutSeconds"] = "15",
            ["TcpServerConfig:Pipeline:ChannelCapacity"] = "2000",
            ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "1",
            ["TcpServerConfig:Security:MaxFramesPerMinutePerIp"] = "600",
            ["TcpServerConfig:Security:InvalidFrameThreshold"] = "40",
            ["TcpServerConfig:Security:BanMinutes"] = "15",
            ["EventBus:Provider"] = "InMemory"
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(data);
        builder.Services.AddTcpServerServices(builder.Configuration);
        using IHost host = builder.Build();
        await host.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream stream = client.GetStream();

        const string login = "*HQ,359586015829802,V0#";
        await SendAsync(stream, login);
        string loginAck = await ReadAckAsync(stream);
        Assert.Equal(login, loginAck);

        const string heartbeat = "*HQ,359586015829802,HTBT,100#";
        await SendAsync(stream, heartbeat);
        string heartbeatAck = await ReadAckAsync(stream);
        Assert.Equal(heartbeat, heartbeatAck);

        await host.StopAsync();
    }

    [Fact]
    public async Task TcpServer_ShouldCloseSessionWhenHandshakeTimeoutExpires()
    {
        int port = GetAvailablePort();
        var data = new Dictionary<string, string?>
        {
            ["TcpServerConfig:Servers:0:Name"] = "TestHandshakeTimeout",
            ["TcpServerConfig:Servers:0:Port"] = port.ToString(),
            ["TcpServerConfig:Servers:0:Protocol"] = "COBAN",
            ["TcpServerConfig:Socket:ReceiveBufferBytes"] = "4096",
            ["TcpServerConfig:Socket:MaxFrameBytes"] = "16384",
            ["TcpServerConfig:Socket:ReadTimeoutSeconds"] = "10",
            ["TcpServerConfig:Socket:IdleTimeoutSeconds"] = "180",
            ["TcpServerConfig:Socket:HandshakeTimeoutSeconds"] = "1",
            ["TcpServerConfig:Pipeline:ChannelCapacity"] = "2000",
            ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "1",
            ["TcpServerConfig:Security:MaxFramesPerMinutePerIp"] = "600",
            ["TcpServerConfig:Security:InvalidFrameThreshold"] = "40",
            ["TcpServerConfig:Security:BanMinutes"] = "15",
            ["EventBus:Provider"] = "InMemory"
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(data);
        builder.Services.AddTcpServerServices(builder.Configuration);
        using IHost host = builder.Build();
        await host.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream stream = client.GetStream();

        await Task.Delay(TimeSpan.FromSeconds(2));

        byte[] buffer = new byte[16];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        Assert.Equal(0, read);

        await host.StopAsync();
    }

    [Fact]
    public async Task TcpServer_ShouldPersistInvalidTrackingAsFailedRawWithoutTelemetryPosition()
    {
        int port = GetAvailablePort();
        const string imei = "359586015829802";
        Guid userId = Guid.NewGuid();
        var data = new Dictionary<string, string?>
        {
            ["TcpServerConfig:Servers:0:Name"] = "TestCobanInvalidTracking",
            ["TcpServerConfig:Servers:0:Port"] = port.ToString(),
            ["TcpServerConfig:Servers:0:Protocol"] = "COBAN",
            ["TcpServerConfig:Socket:ReceiveBufferBytes"] = "4096",
            ["TcpServerConfig:Socket:MaxFrameBytes"] = "16384",
            ["TcpServerConfig:Socket:ReadTimeoutSeconds"] = "30",
            ["TcpServerConfig:Socket:IdleTimeoutSeconds"] = "180",
            ["TcpServerConfig:Socket:HandshakeTimeoutSeconds"] = "15",
            ["TcpServerConfig:Pipeline:ChannelCapacity"] = "2000",
            ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "1",
            ["TcpServerConfig:Security:MaxFramesPerMinutePerIp"] = "600",
            ["TcpServerConfig:Security:InvalidFrameThreshold"] = "40",
            ["TcpServerConfig:Security:BanMinutes"] = "15",
            ["EventBus:Provider"] = "InMemory"
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(data);
        builder.Services.AddTcpServerServices(builder.Configuration);
        using IHost host = builder.Build();

        using (IServiceScope scope = host.Services.CreateScope())
        {
            IUserAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IUserAccountRepository>();
            BindDeviceResult bindResult = await ProvisionAndBindAsync(accounts, userId, imei);
            Assert.Equal(BindDeviceStatus.Bound, bindResult.Status);
        }

        await host.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream stream = client.GetStream();

        await SendAsync(stream, $"imei:{imei},tracker,250208125816,,F,175816.000,A,028,N,07634.01441,W,,;");
        string trackAck = await ReadAckAsync(stream);
        Assert.Equal("ON\r\n", trackAck);

        await WaitForAssertionAsync(async () =>
        {
            using IServiceScope scope = host.Services.CreateScope();
            IOpsRepository opsRepository = scope.ServiceProvider.GetRequiredService<IOpsRepository>();
            ITelemetryQueryRepository telemetryRepository = scope.ServiceProvider.GetRequiredService<ITelemetryQueryRepository>();

            IReadOnlyList<RawPacketRecord> rawPackets = await opsRepository.GetLatestRawPacketsAsync(imei, 10, CancellationToken.None);
            RawPacketRecord raw = Assert.Single(rawPackets);
            Assert.Equal(MessageType.Tracking, raw.MessageType);
            Assert.Equal(RawParseStatus.Failed, raw.ParseStatus);
            Assert.Equal("invalid_latitude", raw.ParseError);
            Assert.True(raw.AckSent);
            Assert.Equal("ON\r\n", raw.AckPayload);

            IReadOnlyList<DevicePositionPointDto> positions = await telemetryRepository.GetPositionsAsync(
                userId,
                imei,
                DateTimeOffset.UtcNow.AddHours(-24),
                DateTimeOffset.UtcNow.AddMinutes(1),
                20,
                CancellationToken.None);
            Assert.Empty(positions);

            IReadOnlyList<TelemetryDeviceSummaryDto> summaries = await telemetryRepository.GetDeviceSummariesAsync(userId, CancellationToken.None);
            TelemetryDeviceSummaryDto summary = Assert.Single(summaries);
            Assert.Null(summary.LastPosition);
        });

        await host.StopAsync();
    }

    private static async Task SendAsync(NetworkStream stream, string payload)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(payload);
        await stream.WriteAsync(bytes);
    }

    private static async Task<string> ReadAckAsync(NetworkStream stream)
    {
        byte[] buffer = new byte[64];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<BindDeviceResult> ProvisionAndBindAsync(IUserAccountRepository accounts, Guid userId, string imei)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        await accounts.EnsureUserProvisioningAsync(userId, "tcp-tests@imptrack.local", "TCP Tests", nowUtc, CancellationToken.None);
        return await accounts.BindDeviceAsync(userId, imei, nowUtc, CancellationToken.None);
    }

    private static async Task WaitForAssertionAsync(Func<Task> assertion, int attempts = 20, int delayMs = 100)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(delayMs);
            }
        }

        throw new Xunit.Sdk.XunitException($"La asercion no se cumplio despues de {attempts} intentos. Ultimo error: {lastException}");
    }
}
