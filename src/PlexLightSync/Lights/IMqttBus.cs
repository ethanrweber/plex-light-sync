namespace PlexLightSync.Lights;

/// <summary>
/// The service's connection to the mqtt broker. The parts that talk mqtt use this interface, so that
/// tests can replace the broker with a fake.
/// </summary>
public interface IMqttBus
{
    /// <summary>Publishes a message. While the bus is not connected, the message is dropped.</summary>
    Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls the handler with the payload of each message on the topic (no wildcards). An empty
    /// payload arrives as "", never as null. The subscription stays after a reconnect. A retained
    /// message arrives at once.
    /// </summary>
    Task SubscribeAsync(string topic, Func<string, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>Runs the action after each connect, so that a part can publish its state again.</summary>
    void OnConnected(Func<CancellationToken, Task> action);
}
