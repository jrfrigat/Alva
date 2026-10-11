using Alva.Api;
using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Автобаланс спринта: сборка задачи, оценка, жадная эвристика и решатель CP-SAT на маленьких командах.</summary>
public sealed class BalanceTests
{
    private static readonly Guid Sprint = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 10, 5);   // понедельник
    private static readonly DateOnly End = new(2026, 10, 16);    // пятница второй недели
    private static readonly Guid Role = Guid.NewGuid();
    private static readonly BalancePersonInput Anna = new(Guid.NewGuid(), "Анна", [Role], []);
    private static readonly BalancePersonInput Boris = new(Guid.NewGuid(), "Борис", [Role], []);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static BalanceTaskInput Task(string title, decimal hours, DateOnly from, DateOnly to, Guid? person = null, DateOnly? deadline = null, Guid? target = null) =>
        new(Guid.NewGuid(), title, "Planned", null, hours, from, to, deadline, target ?? Role, person);

    private static BalanceProblem Problem(string mode, BalanceTaskInput[] tasks, BalanceLinkInput[]? links = null, Func<Guid, CalendarDto>? calendars = null) =>
        BalanceMath.Build(mode, Sprint, Start, End, [Anna, Boris], calendars ?? CapacityMath.DefaultCalendar, tasks, links ?? []);

    // Две недели работы Анны в одной неделе: 80 ч при ёмкости 40.
    private static BalanceTaskInput[] AnnaOverloaded() =>
    [
        Task("API", 40, Start, Start.AddDays(4), Anna.Id),
        Task("Экраны", 40, Start, Start.AddDays(4), Anna.Id)
    ];

    [Fact]
    public void Evaluate_counts_overload_against_daily_capacity()
    {
        var problem = Problem(BalanceModes.Gentle, AnnaOverloaded());

        var before = BalanceMath.Evaluate(problem, new int[problem.Tasks.Length]).Metrics;

        Assert.Equal(40m, before.OverloadHours);
        Assert.Equal(40m, before.PeakOverloadHours);
        Assert.Equal(0, before.Changes);
    }

    [Fact]
    public void Greedy_moves_work_inside_the_role_without_touching_dates()
    {
        var problem = Problem(BalanceModes.Gentle, AnnaOverloaded());

        var choice = BalanceMath.Greedy(problem);
        var proposal = BalanceMath.Proposal(problem, choice, BalanceMath.Heuristic);

        Assert.Equal(0m, proposal.After.OverloadHours);
        var change = Assert.Single(proposal.Changes);
        Assert.Equal(Boris.Id, change.ToPersonId);
        Assert.Equal(Role, change.ToTargetId);
        Assert.Equal(change.FromStart, change.ToStart);
        Assert.Contains("перегруз", change.Reason);
    }

    [Fact]
    public void Solver_relieves_overload_and_gentle_mode_keeps_dates()
    {
        var problem = Problem(BalanceModes.Gentle, AnnaOverloaded());

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.Equal(BalanceMath.Optimal, status);
        Assert.Equal(0m, proposal.After.OverloadHours);
        Assert.Single(proposal.Changes);
        Assert.All(proposal.Changes, c => Assert.Equal(c.FromStart, c.ToStart));
    }

    [Fact]
    public void Deadlines_mode_moves_late_work_earlier()
    {
        var late = Task("Отчёт", 16, Start.AddDays(7), Start.AddDays(8), Anna.Id, deadline: Start.AddDays(3));
        var problem = Problem(BalanceModes.Deadlines, [late]);

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.True(proposal.Before.LateDays > 0);
        Assert.Equal(0, proposal.After.LateDays);
        var change = Assert.Single(proposal.Changes);
        Assert.True(change.ToEnd <= Start.AddDays(3));
        Assert.Contains("срок", change.Reason);
    }

    [Fact]
    public void Finish_to_start_links_are_kept()
    {
        var first = Task("Схема", 16, Start, Start.AddDays(1), Anna.Id);
        var second = Task("Миграция", 16, Start, Start.AddDays(1), Boris.Id);
        var problem = Problem(BalanceModes.Deadlines, [first, second], [new BalanceLinkInput(first.Id, second.Id, "FS", 0)]);

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.True(proposal.Before.LinkViolationDays > 0);
        Assert.Equal(0, proposal.After.LinkViolationDays);
    }

    [Fact]
    public void Absent_people_get_no_work()
    {
        var vacation = new CalendarExceptionDto(Guid.NewGuid(), Start, End, 0m, "Отпуск");
        CalendarDto Calendars(Guid id) => id == Boris.Id ? CapacityMath.DefaultCalendar(id) with { Exceptions = [vacation] } : CapacityMath.DefaultCalendar(id);
        var problem = Problem(BalanceModes.Gentle, AnnaOverloaded(), calendars: Calendars);

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.DoesNotContain(proposal.Changes, c => c.ToPersonId == Boris.Id);
    }

    [Theory]
    [InlineData(BalanceModes.Gentle)]
    [InlineData(BalanceModes.Deadlines)]
    [InlineData(BalanceModes.Maximum)]
    public void Solver_is_never_worse_than_the_current_plan(string mode)
    {
        var tasks = AnnaOverloaded().Append(Task("Отчёт", 16, Start.AddDays(7), Start.AddDays(8), Boris.Id, deadline: Start.AddDays(3))).ToArray();
        var problem = Problem(mode, tasks);

        var (choice, _) = BalanceSolver.Solve(problem, Limit);

        Assert.True(BalanceMath.Evaluate(problem, choice).Objective <= BalanceMath.Evaluate(problem, new int[problem.Tasks.Length]).Objective);
    }

    [Fact]
    public void Tasks_in_progress_parents_and_other_sprints_stay_put()
    {
        var parent = Task("Эпик", 10, Start, Start.AddDays(2), Anna.Id);
        var child = Task("Часть", 8, Start, Start.AddDays(1), Anna.Id) with { ParentId = parent.Id };
        var running = Task("Идёт", 8, Start, Start.AddDays(1), Anna.Id) with { Status = "InProgress" };
        var other = Task("Чужой спринт", 8, End.AddDays(3), End.AddDays(4), Anna.Id);

        var problem = Problem(BalanceModes.Maximum, [parent, child, running, other]);

        Assert.Equal([child.Id], problem.Tasks.Select(t => t.Id));
        Assert.True(problem.Background[0].Sum() > 0);   // остальное — фоновая загрузка Анны
    }
}
