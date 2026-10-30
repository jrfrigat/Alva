using Alva.Client.Models;

namespace Alva.Client.Shared;

/// <summary>
/// Лимит часов в день на задачу: сколько рабочих дней ей нужно и что сказать под полем. Окно короче нужного — не ошибка:
/// работа уйдёт за окончание, риск и автобаланс это покажут.
/// </summary>
public static class DailyLimit
{
    /// <summary>Рабочих дней, нужных оценке при лимите; null — лимита или оценки нет.</summary>
    public static int? DaysNeeded(TaskModel task) =>
        task.DailyHoursLimit is > 0 && task.EstimateHours is > 0 ? (int)Math.Ceiling(task.EstimateHours.Value / task.DailyHoursLimit.Value) : null;

    /// <summary>Подсказка под полем лимита.</summary>
    public static string Hint(TaskModel task)
    {
        if (task.DailyHoursLimit is not > 0) return Strings.T("Пусто — без ограничения. Например, 2 ч в день для задачи, которую ведут параллельно с другой работой.");
        var limit = AlvaFormat.Hours(task.DailyHoursLimit.Value);
        if (DaysNeeded(task) is not { } needed) return string.Format(Strings.T("Не больше {0} ч в день на эту задачу."), limit);
        if (task is { StartDate: { } start, EndDate: { } end } && end >= start && AlvaFormat.WorkDays(start, end) is var have && have < needed)
            return string.Format(Strings.T("При {0} ч в день нужно {1} {2}, а в окне {3} — работа выйдет за окончание."),
                limit, needed, AlvaFormat.Plural(needed, "workday"), have);
        return string.Format(Strings.T("Не больше {0} ч в день: задаче нужно {1} {2}."), limit, needed, AlvaFormat.Plural(needed, "workday"));
    }

    /// <summary>Проверка поля формы; null — значение допустимо.</summary>
    public static string? Error(decimal? limit) =>
        limit is <= 0 or > 24 ? Strings.T("Лимит в день — больше 0 и не больше 24 часов.") : null;
}
