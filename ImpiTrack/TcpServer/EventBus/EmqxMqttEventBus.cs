using System.Text.Json;
using ImpiTrack.Shared.Options;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.EventBus;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace TcpServer.EventBus;

/// <summary>
/// Implementacion de <see cref="IEventBus"/> usando EMQX via MQTTnet.
/// </summary>
public sealed class EmqxMqttEventBus : IEventBus, IHostedService, IAsyncDisposable
{
    private readonly ILogger<EmqxMqttEventBus> _logger;
    private readonly EventBusOptions _options;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _mqttOptions;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    /// <summary>
    /// Crea una instancia de publisher MQTT para EMQX.
    /// </summary>
    /// <param name="logger">Logger estructurado del publisher.</param>
    /// <param name="optionsService">Opciones tipadas del bus de eventos.</param>
    public EmqxMqttEventBus(
        ILogger<EmqxMqttEventBus> logger,
        IGenericOptionsService<EventBusOptions> optionsService)
    {
        _logger = logger;
        _options = optionsService.GetOptions();

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();
        _client.DisconnectedAsync += args =>
        {
            _logger.LogWarning(
                "event_bus_mqtt_disconnected reason={reason}",
                args.ReasonString ?? "unknown");
            return Task.CompletedTask;
        };

        var builder = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            builder = builder.WithCredentials(_options.Username, _options.Password);
        }

        if (_options.UseTls)
        {
            builder = builder.WithTlsOptions(static tls => tls.UseTls(true));
        }

        _mqttOptions = builder.Build();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task PublishAsync<TPayload>(string topic, TPayload payload, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);

        string payloadJson = JsonSerializer.Serialize(payload);
        MqttQualityOfServiceLevel qos = ResolveQoS(topic);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payloadJson)
            .WithQualityOfServiceLevel(qos)
            .Build();

        await _client.PublishAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _connectLock.Dispose();

        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            await _client.ConnectAsync(_mqttOptions, cancellationToken);
            _logger.LogInformation(
                "event_bus_mqtt_connected host={host} port={port} useTls={useTls}",
                _options.Host,
                _options.Port,
                _options.UseTls);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private MqttQualityOfServiceLevel ResolveQoS(string topic)
    {
        int qos = topic.StartsWith("v1/telemetry/", StringComparison.OrdinalIgnoreCase)
            ? _options.TelemetryQoS
            : topic.StartsWith("v1/status/", StringComparison.OrdinalIgnoreCase)
                ? _options.StatusQoS
                : topic.StartsWith("v1/dlq/", StringComparison.OrdinalIgnoreCase)
                    ? _options.DlqQoS
                    : _options.StatusQoS;

        return qos switch
        {
            <= 0 => MqttQualityOfServiceLevel.AtMostOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            _ => MqttQualityOfServiceLevel.ExactlyOnce
        };
    }
}
