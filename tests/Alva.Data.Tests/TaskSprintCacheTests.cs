using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Спринты задачи следуют из дат: правило SprintWindows и кэш TaskSprint, который ведёт AlvaDbContext.</summary>
public sealed class TaskSprintCacheTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 5);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _team = Guid.NewGuid();
    private Sprint _first = null!;
    private Sprint _second = null!;

    [Theory]
    [InlineData(0, 4, true)]     // внутри
    [InlineData(-3, 0, true)]    // касается начала
    [InlineData(13, 20, true)]   // касается конца
    [InlineData(14, 20, false)]  // после
    [InlineData(-5, -1, false)]  // до
    public void A_task_is_in_a_sprint_when_its_window_overlaps(int from, int to, bool inside) =>
        Assert.Equal(inside, SprintWindows.Overlaps(Day.AddDays(from), Day.AddDays(to), Day, Day.AddDays(13)));

    [Fact]
    public void One_date_is_a_one_day_window_and_no_dates_is_the_backlog()
    {
        Assert.True(SprintWindows.Overlaps(Day.AddDays(3), null, Day, Day.AddDays(13)));
        Assert.True(SprintWindows.Overlaps(null, Day.AddDays(3), Day, Day.AddDays(13)));
        Assert.False(SprintWindows.Overlaps(null, null, Day, Day.AddDays(13)));
        Assert.Null(SprintWindows.Window(null, null));
    }

    [Fact]
    public async Task Saving_a_task_puts_it_in_every_sprint_its_dates_cross()
    {
        var task = await Add(new TaskCard { TeamId = _team, Title = "Стык", StartDate = Day.AddDays(10), EndDate = Day.AddDays(16) });

        Assert.Equal([_first.Id, _second.Id], await SprintsOf(task));
    }

    [Fact]
    public async Task Moving_dates_moves_the_task_and_clearing_them_returns_it_to_the_backlog()
    {
        var task = await Add(new TaskCard { TeamId = _team, Title = "Задача", StartDate = Day, EndDate = Day.AddDays(2) });
        Assert.Equal([_first.Id], await SprintsOf(task));

        await Change(task, t => { t.StartDate = Day.AddDays(15); t.EndDate = Day.AddDays(17); });
        Assert.Equal([_second.Id], await SprintsOf(task));

        await Change(task, t => { t.StartDate = null; t.EndDate = null; });
        Assert.Empty(await SprintsOf(task));
    }

    [Fact]
    public async Task An_archived_task_is_in_no_sprint()
    {
        var task = await Add(new TaskCard { TeamId = _team, Title = "Задача", StartDate = Day, EndDate = Day.AddDays(2) });

        await Change(task, t => t.Archived = true);

        Assert.Empty(await SprintsOf(task));
    }

    [Fact]
    public async Task Changing_sprint_dates_recounts_only_its_tasks()
    {
        var early = await Add(new TaskCard { TeamId = _team, Title = "Ранняя", StartDate = Day, EndDate = Day.AddDays(1) });
        var late = await Add(new TaskCard { TeamId = _team, Title = "Поздняя", StartDate = Day.AddDays(20), EndDate = Day.AddDays(21) });

        await Using(async db =>
        {
            var sprint = await db.Sprints.SingleAsync(x => x.Id == _first.Id);
            sprint.StartDate = Day.AddDays(18); sprint.EndDate = Day.AddDays(25);
            await db.SaveChangesAsync();
        });

        Assert.Empty(await SprintsOf(early));
        Assert.Equal([_second.Id, _first.Id], await SprintsOf(late)); // первый теперь начинается позже второго
    }

    [Fact]
    public async Task A_new_sprint_takes_the_tasks_already_in_its_window()
    {
        var task = await Add(new TaskCard { TeamId = _team, Title = "Будущее", StartDate = Day.AddDays(40), EndDate = Day.AddDays(41) });
        var third = new Sprint { TeamId = _team, Name = "Третий", StartDate = Day.AddDays(35), EndDate = Day.AddDays(48) };

        await Using(async db => { db.Sprints.Add(third); await db.SaveChangesAsync(); });

        Assert.Equal([third.Id], await SprintsOf(task));
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _first = new Sprint { TeamId = _team, Name = "Первый", StartDate = Day, EndDate = Day.AddDays(13) };
        _second = new Sprint { TeamId = _team, Name = "Второй", StartDate = Day.AddDays(14), EndDate = Day.AddDays(27) };
        await Using(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            var workspace = new Workspace { Name = "W" };
            db.Workspaces.Add(workspace);
            db.Teams.Add(new Team { Id = _team, WorkspaceId = workspace.Id, Name = "T" });
            db.Sprints.AddRange(_first, _second);
            await db.SaveChangesAsync();
        });
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private async Task Using(Func<AlvaDbContext, Task> action)
    {
        await using var db = new AlvaDbContext(new DbContextOptionsBuilder<AlvaDbContext>().UseSqlite(_connection).Options);
        await action(db);
    }

    private async Task<TaskCard> Add(TaskCard task)
    {
        await Using(async db => { db.Tasks.Add(task); await db.SaveChangesAsync(); });
        return task;
    }

    private Task Change(TaskCard task, Action<TaskCard> change) => Using(async db =>
    {
        var stored = await db.Tasks.SingleAsync(x => x.Id == task.Id);
        change(stored);
        await db.SaveChangesAsync();
    });

    private async Task<Guid[]> SprintsOf(TaskCard task)
    {
        Guid[] result = [];
        await Using(async db => result = [.. (await db.TaskSprints.Where(x => x.TaskId == task.Id)
            .Join(db.Sprints, x => x.SprintId, s => s.Id, (x, s) => new { x.SprintId, s.StartDate }).ToListAsync())
            .OrderBy(x => x.StartDate).Select(x => x.SprintId)]);
        return result;
    }
}
