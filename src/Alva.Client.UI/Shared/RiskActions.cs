using Alva.Contracts;

namespace Alva.Client.Shared;

/// <summary>Куда ведёт причина риска: к блокирующей задаче, календарю исполнителя или карточке задачи.</summary>
public static class RiskActions
{
    /// <summary>Адрес действия для причины; без конкретного человека или блокера — карточка задачи.</summary>
    public static string Href(ReasonDto reason, Guid taskId, Guid? personId = null) => reason.Code switch
    {
        ReasonCodes.RiskDependencyOpen when Blocker(reason) is { } blocker => $"tasks/{blocker}",
        ReasonCodes.RiskAbsence or ReasonCodes.RiskOverload when personId is { } person => $"capacity/{person}",
        _ => $"tasks/{taskId}"
    };

    /// <summary>Подпись действия для title и aria-label.</summary>
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
