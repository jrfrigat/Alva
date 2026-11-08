using Alva.Contracts;

namespace Alva.Client.Shared;

/// <summary>Where a risk reason leads: to the blocking task, the assignee's calendar or the task card.</summary>
public static class RiskActions
{
    /// <summary>The action address for the reason; without a specific person or blocker, the task card.</summary>
    public static string Href(ReasonDto reason, Guid taskId, Guid? personId = null) => reason.Code switch
    {
        ReasonCodes.RiskDependencyOpen when Blocker(reason) is { } blocker => $"tasks/{blocker}",
        ReasonCodes.RiskAbsence or ReasonCodes.RiskOverload when personId is { } person => $"capacity/{person}",
        _ => $"tasks/{taskId}"
    };

    /// <summary>The action label for the title and aria-label.</summary>
    public static string Label(ReasonDto reason, Guid? personId = null) => reason.Code switch
    {
        ReasonCodes.RiskDependencyOpen => AlvaStrings.Risks_OpenTheBlockingTask,
        ReasonCodes.RiskAbsence or ReasonCodes.RiskOverload when personId is not null => AlvaStrings.Risks_OpenTheAssigneeSCalendar,
        ReasonCodes.RiskNoAssignee or ReasonCodes.RiskRequiredPerson => AlvaStrings.Common_AssignAnAssignee,
        _ => AlvaStrings.Risks_OpenTask
    };

    private static Guid? Blocker(ReasonDto reason) =>
        reason.Args is { Length: > 0 } args && Guid.TryParse(args[0], out var id) ? id : null;
}
