namespace Alva.Contracts;

/// <summary>A task written by another client. Pages reload the task or its list when this arrives;
/// the version lets them ignore an event older than the copy they already have.</summary>
/// <param name="TeamId">Team whose task changed.</param>
/// <param name="TaskId">Changed task.</param>
/// <param name="Version">Version of the task after the write.</param>
/// <param name="Kind">updated or deleted.</param>
public sealed record TaskChangedEvent(Guid TeamId, Guid TaskId, long Version, string Kind);
