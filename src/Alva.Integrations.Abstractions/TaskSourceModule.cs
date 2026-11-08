namespace Alva.Integrations;

/// <summary>
/// An integration module: where tasks come from (Yandex Tracker, Jira, ...). The module describes its own settings, and the core stores them
/// as is and does not know their meaning - this way a new module needs no changes in the core.
/// </summary>
public interface ITaskSourceModule
{
    /// <summary>A stable identifier: it is stored in connections, and changing it is not allowed.</summary>
    string Id { get; }
    string Name { get; }
    string Description { get; }
    IReadOnlyList<IntegrationSetting> Settings { get; }
    IntegrationCapabilities Capabilities { get; }

    /// <summary>A task source for a specific connection. Invalid settings throw <see cref="IntegrationException"/>.</summary>
    Task<ITaskSource> ConnectAsync(IntegrationConnection connection, CancellationToken ct);
}

/// <summary>Tasks of a single connection.</summary>
public interface ITaskSource
{
    /// <summary>A connection check: whether access exists and under which account.</summary>
    Task<IntegrationCheck> CheckAsync(CancellationToken ct);

    /// <summary>
    /// Tasks changed after <paramref name="changedSince"/>; null means all tasks of the connection. The source may return a
    /// little more (for example, with day precision): the core updates tasks idempotently.
    /// </summary>
    IAsyncEnumerable<ExternalTask> ReadTasksAsync(DateTimeOffset? changedSince, CancellationToken ct);
}

/// <summary>What the module can bring.</summary>
[Flags]
public enum IntegrationCapabilities
{
    None = 0,
    Tasks = 1,
    Links = 2,
    Hierarchy = 4,
    Estimates = 8,
    Assignees = 16,
    /// <summary>Can return only what changed since the last time.</summary>
    Incremental = 32
}

/// <summary>A connection as the core stores it: settings by schema keys and secrets separately.</summary>
public sealed record IntegrationConnection(Guid Id, string Name, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Secrets)
{
    /// <summary>The value of a setting or secret; empty means null.</summary>
    public string? Value(string key) =>
        (Settings.TryGetValue(key, out var value) || Secrets.TryGetValue(key, out value)) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>The result of a connection check.</summary>
/// <param name="Account">The account the connection runs as - so a person can see whether it is the right token.</param>
public sealed record IntegrationCheck(bool Ok, string Message, string? Account = null);

/// <summary>A module error with human-readable text: the core shows it as is.</summary>
public sealed class IntegrationException(string message, Exception? inner = null) : Exception(message, inner);
