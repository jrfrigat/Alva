using Alva.Client.Services;
using Alva.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Alva.Client.Services;

/// <summary>The production realtime: one hub connection per tab, subscribed to the current team.</summary>
public sealed class RealtimeService(IApiService api, ClientIdentity identity, NavigationManager navigation) : IRealtimeService
{
    private static readonly TimeSpan[] Retries =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private HubConnection? _connection;
    private Guid? _teamId;
    private Task? _starting;

    public event Action<TaskChangedEvent>? TaskChanged;
    public event Action? Resynced;

    public bool Connected => _connection?.State == HubConnectionState.Connected;

    public Task EnsureStarted()
    {
        if (api.User is null || api.TeamId is not { } teamId) return Task.CompletedTask;
        if (Connected && _teamId == teamId) return Task.CompletedTask;
        return _starting ??= Start(teamId);
    }

    private async Task Start(Guid teamId)
    {
        try
        {
            await Stop();
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(new Uri(navigation.BaseUri), $"api/hubs/team?clientId={identity.Id}"))
                .WithAutomaticReconnect(Retries)
                .Build();
            connection.On<TaskChangedEvent>("TaskChanged", change => TaskChanged?.Invoke(change));
            connection.Reconnected += async _ => { await Subscribe(); Resynced?.Invoke(); };
            connection.Closed += _ =>
            {
                if (ReferenceEquals(_connection, connection)) { _connection = null; _teamId = null; }
                return Task.CompletedTask;
            };
            _connection = connection;
            _teamId = teamId;
            await connection.StartAsync();
            await Subscribe();
        }
        catch (Exception)
        {
            // Realtime is an enhancement: an unreachable hub must not keep a page from loading.
            var failed = _connection;
            _connection = null;
            _teamId = null;
            if (failed is not null) await failed.DisposeAsync();
        }
        finally { _starting = null; }
    }

    private async Task Subscribe()
    {
        if (_connection is null || _teamId is not { } teamId) return;
        try { await _connection.InvokeAsync("Subscribe", teamId); }
        catch (Exception)
        {
            // Access can be revoked while the tab is open; the next reconnect tries again.
        }
    }

    public async Task Stop()
    {
        var connection = _connection;
        _connection = null;
        _teamId = null;
        if (connection is not null) await connection.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(Stop());
}
