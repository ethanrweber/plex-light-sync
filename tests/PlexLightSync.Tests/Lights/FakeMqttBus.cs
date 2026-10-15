using PlexLightSync.Lights;

namespace PlexLightSync.Tests.Lights;

/// <summary>An in-memory broker: it records what the service publishes, and delivers test messages.</summary>
public sealed class FakeMqttBus : IMqttBus
{
    readonly Dictionary<string, Func<string, Task>> _handlers = [];
    readonly List<Func<CancellationToken, Task>> _onConnected = [];

    public List<(string Topic, string Payload, bool Retain)> Published { get; } = [];

    public IReadOnlyCollection<string> Subscriptions => _handlers.Keys;

    public Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default)
    {
        Published.Add((topic, payload, retain));
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string topic, Func<string, Task> handler, CancellationToken cancellationToken = default)
    {
        _handlers[topic] = handler;
        return Task.CompletedTask;
    }

    public void OnConnected(Func<CancellationToken, Task> action) => _onConnected.Add(action);

    /// <summary>Acts like a connect: runs the actions that the parts registered.</summary>
    public async Task ConnectAsync()
    {
        foreach (var action in _onConnected)
            await action(CancellationToken.None);
    }

    /// <summary>Delivers a message to the subscriber of the topic.</summary>
    public Task DeliverAsync(string topic, string payload) =>
        _handlers.TryGetValue(topic, out var handler) ? handler(payload) : throw new InvalidOperationException($"no subscription for {topic}");

    public IEnumerable<string> PayloadsFor(string topic) =>
        Published.Where(message => message.Topic == topic).Select(message => message.Payload);
}
