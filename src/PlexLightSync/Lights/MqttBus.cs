using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace PlexLightSync.Lights;

/// <summary>
/// Keeps one connection to the mqtt broker. Every 5 s it checks the connection, and connects again
/// if needed.
/// </summary>
/// <remarks>
/// The connection carries a last will: if the service dies, the broker publishes "offline" to
/// <see cref="AvailabilityTopic"/>, and home assistant shows the service's entities as unavailable.
/// </remarks>
public sealed class MqttBus(IOptions<MqttOptions> options, ILogger<MqttBus> logger) : BackgroundService, IMqttBus
{
    public const string AvailabilityTopic = "plex-light-sync/availability";
    const string ClientId = "plex-light-sync";
    static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    readonly ConcurrentDictionary<string, Func<string, Task>> _handlers = new();
    readonly List<Func<CancellationToken, Task>> _onConnected = [];
    bool _connectFailed;
    volatile bool _stopping;

    public void OnConnected(Func<CancellationToken, Task> action) => _onConnected.Add(action);

    public async Task SubscribeAsync(string topic, Func<string, Task> handler, CancellationToken cancellationToken = default)
    {
        _handlers[topic] = handler;
        // before the connect, the connect subscribes; after it, subscribe now
        if (_client.IsConnected)
            await _client.SubscribeAsync(topic, cancellationToken: cancellationToken);
    }

    public async Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected)
            return;

        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).Build();
        await _client.PublishAsync(message, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await _client.TryPingAsync(stoppingToken))
                    await ConnectAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                if (!_connectFailed)
                    logger.LogWarning("MQTT connect failed, retrying every {Interval} s: {Error}", CheckInterval.TotalSeconds, ex.Message);
                _connectFailed = true;
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId(ClientId)
            .WithCleanSession()
            .WithWillTopic(AvailabilityTopic)
            .WithWillPayload("offline")
            .WithWillRetain();
        if (settings.User is not null)
            builder.WithCredentials(settings.User, settings.Password);

        var result = await _client.ConnectAsync(builder.Build(), cancellationToken);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
            throw new InvalidOperationException($"the broker refused the connection: {result.ResultCode}");

        logger.LogInformation("MQTT connected to {Host}:{Port}", settings.Host, settings.Port);
        _connectFailed = false;

        foreach (var topic in _handlers.Keys)
            await _client.SubscribeAsync(topic, cancellationToken: cancellationToken);
        await PublishAsync(AvailabilityTopic, "online", retain: true, cancellationToken);
        foreach (var action in _onConnected)
            await action(cancellationToken);
    }

    async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        if (!_handlers.TryGetValue(topic, out var handler))
            return;

        try
        {
            // mqttnet gives null for an empty payload; handlers always get a string
            await handler(args.ApplicationMessage.ConvertPayloadToString() ?? "");
        }
        catch (Exception ex)
        {
            // one bad message must not stop the connection
            logger.LogError(ex, "Handler for MQTT topic {Topic} failed", topic);
        }
    }

    Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (args.ClientWasConnected && !_stopping)
            logger.LogWarning("MQTT disconnected ({Reason}), reconnecting", args.Reason);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // a clean disconnect sends no last will, so say "offline" first
        _stopping = true;
        if (_client.IsConnected)
        {
            await PublishAsync(AvailabilityTopic, "offline", retain: true, cancellationToken);
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}
