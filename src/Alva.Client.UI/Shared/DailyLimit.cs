using Alva.Client.Models;

namespace Alva.Client.Shared;

/// <summary>
/// The daily hours limit for a task: how many workdays it needs and what to say under the field. A window
/// shorter than needed is not an error: the work will run past the finish, and the risk view and auto-balance will show it.
/// </summary>
public static class DailyLimit
{
    /// <summary>The workdays the estimate needs at the limit; null means there is no limit or no estimate.</summary>
    public static int? DaysNeeded(TaskModel task) =>
        task.DailyHoursLimit is > 0 && task.EstimateHours is > 0 ? (int)Math.Ceiling(task.EstimateHours.Value / task.DailyHoursLimit.Value) : null;

    /// <summary>The hint under the limit field.</summary>
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

    /// <summary>Form field validation; null means the value is valid.</summary>
    public static string? Error(decimal? limit) =>
        limit is <= 0 or > 24 ? AlvaStrings.Common_TheDailyLimitMustBeAbove0AndNoMoreThan24Hours : null;
}
