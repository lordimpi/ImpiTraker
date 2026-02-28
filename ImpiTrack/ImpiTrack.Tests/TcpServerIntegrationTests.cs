using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
            ["TcpServerConfig:Pipeline:ConsumerWorkers"] = "1"
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
            ["TcpServerConfig:Security:BanMinutes"] = "15"
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
            ["TcpServerConfig:Security:BanMinutes"] = "15"
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
}
