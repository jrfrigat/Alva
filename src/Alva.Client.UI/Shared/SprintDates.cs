using Alva.Client.Models;
using Alva.Contracts;

namespace Alva.Client.Shared;

/// <summary>
/// Спринт в редакторе задачи — не поле, а следствие плановых дат. Здесь он считается по датам, которые правят прямо
/// сейчас (до сохранения), а выбор спринта ставит задаче его окно; «бэклог» снимает даты.
/// </summary>
public static class SprintDates
{
    public static IEnumerable<SprintModel> Of(TaskModel task, IEnumerable<SprintModel> sprints) =>
        SprintWindows.Of(task.StartDate, task.EndDate, sprints, s => s.StartDate, s => s.EndDate);

    /// <summary>Первый спринт задачи по её датам — его показывает выбор спринта; null — бэклог.</summary>
    public static Guid? First(TaskModel task, IEnumerable<SprintModel> sprints) => Of(task, sprints).FirstOrDefault()?.Id;

    /// <summary>Подсказка под выбором: все спринты задачи, если их несколько.</summary>
    public static string? Hint(TaskModel task, IEnumerable<SprintModel> sprints) =>
        Of(task, sprints).Select(s => s.Name).ToArray() is { Length: > 1 } names ? Strings.T("Идёт в спринтах: ") + string.Join(", ", names) : null;

    /// <summary>Ставит задаче окно спринта или, без спринта, снимает плановые даты.</summary>
    public static void Place(TaskModel task, SprintModel? sprint)
    {
        task.StartDate = sprint?.StartDate;
        task.EndDate = sprint?.EndDate;
    }
}
