using Alva.Client.Models;
using Alva.Contracts;

namespace Alva.Client.Shared;

/// <summary>
/// A sprint in the task editor is not a field but a consequence of the planned dates. Here it is computed from the dates being edited right
/// now (before saving), and choosing a sprint gives the task its window; the backlog option clears the dates.
/// </summary>
public static class SprintDates
{
    public static IEnumerable<SprintModel> Of(TaskModel task, IEnumerable<SprintModel> sprints) =>
        SprintWindows.Of(task.StartDate, task.EndDate, sprints, s => s.StartDate, s => s.EndDate);

    /// <summary>The task's first sprint by its dates - the sprint selector shows it; null means the backlog.</summary>
    public static Guid? First(TaskModel task, IEnumerable<SprintModel> sprints) => Of(task, sprints).FirstOrDefault()?.Id;

    /// <summary>The hint under the selector: all the task's sprints, if there are several.</summary>
    public static string? Hint(TaskModel task, IEnumerable<SprintModel> sprints) =>
        Of(task, sprints).Select(s => s.Name).ToArray() is { Length: > 1 } names ? AlvaStrings.Common_RunsInSprints + string.Join(", ", names) : null;

    /// <summary>Gives the task the sprint's window or, without a sprint, clears the planned dates.</summary>
    public static void Place(TaskModel task, SprintModel? sprint)
    {
        task.StartDate = sprint?.StartDate;
        task.EndDate = sprint?.EndDate;
    }
}
