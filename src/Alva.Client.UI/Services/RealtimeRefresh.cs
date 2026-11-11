namespace Alva.Client.Services;

/// <summary>
/// Turns a burst of realtime events into one quiet reload. A draft holds the reload back: replacing
/// the data under an open editor would lose the edits, and the save reports the version conflict.
/// </summary>
public sealed class RealtimeRefresh(Func<bool> canApply)
{
    private CancellationTokenSource? _delay;

    public void Schedule(Func<Task> reload)
    {
        _delay?.Cancel();
        var delay = new CancellationTokenSource();
        _delay = delay;
        _ = Run(delay.Token, reload);
    }

    private async Task Run(CancellationToken token, Func<Task> reload)
    {
        try { await Task.Delay(150, token); }
        catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested || !canApply()) return;
        try { await reload(); }
        catch (Exception)
        {
            // A quiet refresh is an enhancement: the next event or the next navigation tries again.
        }
    }

    public void Cancel()
    {
        _delay?.Cancel();
        _delay = null;
    }
}
