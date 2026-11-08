using Alva.Contracts;
using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Alva.Infrastructure;

/// <summary>
/// TaskSprint cache recalculation. It touches only what changed: rows of a changed task are recalculated against the sprints of its
/// team, and rows of a changed sprint against the tasks whose window overlaps its new window (a query by the date index).
/// </summary>
internal static class TaskSprintCache
{
    private static readonly string[] TaskFields = [nameof(TaskCard.StartDate), nameof(TaskCard.EndDate), nameof(TaskCard.Archived), nameof(TaskCard.TeamId)];
    private static readonly string[] SprintFields = [nameof(Sprint.StartDate), nameof(Sprint.EndDate), nameof(Sprint.TeamId)];

    /// <summary>Tasks and sprints the cache depends on, among the context changes.</summary>
    public static (HashSet<Guid> Tasks, HashSet<Guid> Sprints) Touched(ChangeTracker tracker)
    {
        var tasks = new HashSet<Guid>();
        var sprints = new HashSet<Guid>();
        foreach (var entry in tracker.Entries())
        {
            if (entry.Entity is TaskCard task && Affects(entry, TaskFields)) tasks.Add(task.Id);
            else if (entry.Entity is Sprint sprint && Affects(entry, SprintFields)) sprints.Add(sprint.Id);
        }
        return (tasks, sprints);
    }

    private static bool Affects(EntityEntry entry, string[] fields) => entry.State switch
    {
        EntityState.Added or EntityState.Deleted => true,
        EntityState.Modified => fields.Any(f => entry.Property(f).IsModified),
        _ => false
    };

    /// <summary>Removes the rows of affected tasks and sprints - before saving, so deleting a task or sprint does not run into the cache.</summary>
    public static Task Forget(AlvaDbContext db, HashSet<Guid> tasks, HashSet<Guid> sprints, CancellationToken ct) =>
        db.TaskSprints.Where(x => tasks.Contains(x.TaskId) || sprints.Contains(x.SprintId)).ExecuteDeleteAsync(ct);

    /// <summary>Inserts the rows of affected tasks and sprints using the already saved dates.</summary>
    public static async Task Fill(AlvaDbContext db, HashSet<Guid> tasks, HashSet<Guid> sprints, CancellationToken ct)
    {
        var pairs = new HashSet<(Guid Task, Guid Sprint)>();
        var rows = new List<TaskSprint>();
        void Add(Guid team, Guid task, Guid sprint)
        {
            if (pairs.Add((task, sprint))) rows.Add(new TaskSprint { TeamId = team, TaskId = task, SprintId = sprint });
        }

        if (tasks.Count > 0)
        {
            var changed = await db.Tasks.AsNoTracking().Where(x => tasks.Contains(x.Id) && !x.Archived && (x.StartDate != null || x.EndDate != null))
                .Select(x => new { x.Id, x.TeamId, x.StartDate, x.EndDate }).ToListAsync(ct);
            var teams = changed.Select(x => x.TeamId).Distinct().ToArray();
            var teamSprints = (await db.Sprints.AsNoTracking().Where(x => teams.Contains(x.TeamId))
                .Select(x => new { x.Id, x.TeamId, x.StartDate, x.EndDate }).ToListAsync(ct)).ToLookup(x => x.TeamId);
            foreach (var task in changed)
                foreach (var sprint in teamSprints[task.TeamId])
                    if (SprintWindows.Overlaps(task.StartDate, task.EndDate, sprint.StartDate, sprint.EndDate)) Add(task.TeamId, task.Id, sprint.Id);
        }

        if (sprints.Count > 0)
        {
            var changed = await db.Sprints.AsNoTracking().Where(x => sprints.Contains(x.Id)).ToListAsync(ct);
            foreach (var sprint in changed)
            {
                var (start, end) = (sprint.StartDate, sprint.EndDate);
                var inWindow = await db.Tasks.AsNoTracking()
                    .Where(x => x.TeamId == sprint.TeamId && !x.Archived && (x.StartDate != null || x.EndDate != null)
                        && (x.StartDate ?? x.EndDate) <= end && (x.EndDate ?? x.StartDate) >= start)
                    .Select(x => new { x.Id, x.StartDate, x.EndDate }).ToListAsync(ct);
                foreach (var task in inWindow)
                    if (SprintWindows.Overlaps(task.StartDate, task.EndDate, start, end)) Add(sprint.TeamId, task.Id, sprint.Id);
            }
        }

        db.TaskSprints.AddRange(rows);
    }
}
