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
