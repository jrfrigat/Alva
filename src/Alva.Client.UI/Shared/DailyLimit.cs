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
        if (task.DailyHoursLimit is not > 0) return AlvaStrings.Common_EmptyMeansNoLimitForExample2HADayForATaskDoneAlongsideOtherWork;
        var limit = AlvaFormat.Hours(task.DailyHoursLimit.Value);
        if (DaysNeeded(task) is not { } needed) return string.Format(AlvaStrings.Common_NoMoreThanHADayOnThisTask, limit);
        if (task is { StartDate: { } start, EndDate: { } end } && end >= start && AlvaFormat.WorkDays(start, end) is var have && have < needed)
            return string.Format(AlvaStrings.Common_AtHADayItNeedsButTheWindowHasTheWorkWillRunPastTheFinishDate,
                limit, needed, AlvaFormat.Plural(needed, "workday"), have);
        return string.Format(AlvaStrings.Common_NoMoreThanHADayTheTaskNeeds, limit, needed, AlvaFormat.Plural(needed, "workday"));
    }

    /// <summary>Проверка поля формы; null — значение допустимо.</summary>
    public static string? Error(decimal? limit) =>
        limit is <= 0 or > 24 ? AlvaStrings.Common_TheDailyLimitMustBeAbove0AndNoMoreThan24Hours : null;
}
