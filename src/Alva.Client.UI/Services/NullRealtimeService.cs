using Alva.Contracts;

namespace Alva.Client.Services;

/// <summary>The serverless demo has no second client to hear from.</summary>
public sealed class NullRealtimeService : IRealtimeService
{
    public event Action<TaskChangedEvent>? TaskChanged { add { } remove { } }
    public event Action? Resynced { add { } remove { } }
    public bool Connected => false;
    public Task EnsureStarted() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
