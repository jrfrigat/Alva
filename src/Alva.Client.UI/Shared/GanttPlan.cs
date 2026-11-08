namespace Alva.Client.Shared;

/// <summary>What is dragged on the planning board: a task or a person.</summary>
public abstract record PlanDrag;

/// <summary>A task from the backlog or from a Gantt row.</summary>
public sealed record TaskDrag(Guid TaskId) : PlanDrag;

/// <summary>A person from the Sprint people block.</summary>
public sealed record PersonDrag(Guid PersonId) : PlanDrag;
