using Alva.Client.Services;
using Alva.Contracts;

namespace Alva.Client.UI.Tests;

/// <summary>Lets a test raise the event a live team hub would send.</summary>
public sealed class FakeRealtimeService : IRealtimeService
{
    public event Action<TaskChangedEvent>? TaskChanged;
    public event Action? Resynced;
    public bool Connected { get; private set; }
    public int Starts { get; private set; }

    public Task EnsureStarted()
    {
        Starts++;
        Connected = true;
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        Connected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Change(TaskChangedEvent change) => TaskChanged?.Invoke(change);

    public void Resync() => Resynced?.Invoke();
}
