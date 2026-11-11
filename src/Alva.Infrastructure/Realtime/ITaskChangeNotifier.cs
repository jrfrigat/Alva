namespace Alva.Infrastructure;

/// <summary>What happened to one task when a unit of work was committed.</summary>
public sealed record TaskChange(Guid TeamId, Guid TaskId, long Version, TaskChangeKind Kind);

/// <summary>Updated is a write of a task row; Deleted is a hard removal of one.</summary>
public enum TaskChangeKind
{
    Updated,
    Deleted
}

/// <summary>
/// Receives task changes after a successful save. The database raises them so no write path can skip
/// them - an edit, a move, an assignment, an import and a background integration all save through it.
/// A transport failure must not fail the write.
/// </summary>
public interface ITaskChangeNotifier
{
    Task Notify(IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default);
}
