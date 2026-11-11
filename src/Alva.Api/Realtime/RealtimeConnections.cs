using System.Collections.Concurrent;

namespace Alva.Api;

/// <summary>Maps the tab id a client sends with every write to its live hub connections, so the
/// author's own change is left out of the broadcast.</summary>
public sealed class RealtimeConnections
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _byClient = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _clientByConnection = new(StringComparer.Ordinal);

    public void Add(string clientId, string connectionId)
    {
        var connections = _byClient.GetOrAdd(clientId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        connections[connectionId] = 0;
        _clientByConnection[connectionId] = clientId;
    }

    public void Remove(string connectionId)
    {
        if (!_clientByConnection.TryRemove(connectionId, out var clientId)) return;
        if (_byClient.TryGetValue(clientId, out var connections)) connections.TryRemove(connectionId, out _);
    }

    public IReadOnlyList<string> Connections(string clientId) =>
        _byClient.TryGetValue(clientId, out var connections) ? connections.Keys.ToArray() : [];
}
