using Alva.Contracts;

namespace Alva.Client.Services;

/// <summary>Team realtime: task changes pushed by the server, and a resync signal after a reconnect.</summary>
public interface IRealtimeService : IAsyncDisposable
{
    /// <summary>A task of the current team changed on the server.</summary>
    event Action<TaskChangedEvent>? TaskChanged;

    /// <summary>The connection came back after an interruption: pages reload so they miss nothing.</summary>
    event Action? Resynced;

    bool Connected { get; }

    /// <summary>Starts the connection for the current team; repeated calls do nothing.</summary>
    Task EnsureStarted();

    Task Stop();
}
