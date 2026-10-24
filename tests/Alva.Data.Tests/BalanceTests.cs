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

    private static BalanceTaskInput Task(string title, decimal hours, DateOnly from, DateOnly to, Guid? person = null, DateOnly? deadline = null, Guid? target = null, bool datesFixed = false) =>
        new(Guid.NewGuid(), title, "Planned", null, hours, from, to, deadline, target ?? Role, person, DatesFixed: datesFixed);

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

    [Fact]
    public void Tasks_the_balance_cannot_move_are_reported_with_a_reason()
    {
        var unassigned = new BalanceTaskInput(Guid.NewGuid(), "Без назначения", "Planned", null, 8, Start, Start.AddDays(1), null, null, null);
        var started = new BalanceTaskInput(Guid.NewGuid(), "В работе", "InProgress", null, 8, Start, Start.AddDays(1), null, Role, Anna.Id);
        var movable = Task("Обычная", 8, Start, Start.AddDays(1), Anna.Id);
        var problem = Problem(BalanceModes.Gentle, [unassigned, started, movable]);

        var proposal = BalanceMath.Proposal(problem, BalanceMath.Greedy(problem), BalanceMath.Heuristic);

        Assert.Equal(1, proposal.Considered);
        Assert.Equal("Нет назначения — ни человека, ни роли", proposal.Excluded!.Single(x => x.TaskId == unassigned.Id).Reason);
        Assert.Equal("В работе — даты и исполнитель не меняются", proposal.Excluded!.Single(x => x.TaskId == started.Id).Reason);
    }

    [Fact]
    public void A_fixed_task_keeps_its_start_but_may_change_person()
    {
        var fixedTask = Task("Фикс", 16, Start, Start.AddDays(1), Anna.Id, datesFixed: true);
        var problem = Problem(BalanceModes.Maximum, [fixedTask]);
        var index = Array.FindIndex(problem.Tasks, t => t.Id == fixedTask.Id);

        Assert.All(problem.Tasks[index].Options, option => Assert.Equal(0, option.Start));
        // В «Максимуме» доступен человек с общей ролью: даты те же, исполнитель может смениться.
        Assert.Contains(problem.Tasks[index].Options, option => option.Person == Array.FindIndex(problem.People, p => p.Id == Boris.Id));

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        Assert.NotEqual(BalanceMath.Infeasible, status);
        Assert.Equal(0, problem.Tasks[index].Options[choice[index]].Start);
    }

    [Theory]
    [InlineData("FS")]
    [InlineData("SS")]
    [InlineData("FF")]
    [InlineData("SF")]
    public void Hard_links_hold_for_every_kind(string kind)
    {
        // Вторая задача стоит раньше первой — исходный план нарушает любую из четырёх связей.
        var first = Task("Первая", 16, Start.AddDays(4), Start.AddDays(5), Anna.Id);
        var second = Task("Вторая", 16, Start.AddDays(1), Start.AddDays(2), Boris.Id);
        var problem = Problem(BalanceModes.Deadlines, [first, second],
            [new BalanceLinkInput(first.Id, second.Id, kind, 0)]);

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.NotEqual(BalanceMath.Infeasible, status);
        Assert.Equal(0, proposal.After.LinkViolationDays);
    }

    [Fact]
    public void A_running_task_anchors_the_successor_even_though_it_does_not_move()
    {
        var running = new BalanceTaskInput(Guid.NewGuid(), "Идёт", "InProgress", null, 16,
            Start.AddDays(4), Start.AddDays(5), null, Role, Anna.Id);
        var successor = Task("После", 16, Start.AddDays(1), Start.AddDays(2), Boris.Id);
        var problem = Problem(BalanceModes.Deadlines, [running, successor],
            [new BalanceLinkInput(running.Id, successor.Id, "FS", 0)]);
        var index = Array.FindIndex(problem.Tasks, t => t.Id == successor.Id);

        Assert.Null(problem.Infeasible);
        var (choice, _) = BalanceSolver.Solve(problem, Limit);

        // Предшественник занимает 4–5-й дни, эксклюзивный конец — 6-й: последователь не раньше 6-го.
        Assert.True(problem.Tasks[index].Options[choice[index]].Start >= 6);
    }

    [Fact]
    public void Two_fixed_tasks_that_contradict_a_link_are_infeasible()
    {
        var first = Task("Фикс А", 16, Start, Start.AddDays(1), Anna.Id, datesFixed: true);
        var second = Task("Фикс Б", 16, Start, Start.AddDays(1), Boris.Id, datesFixed: true);
        var problem = Problem(BalanceModes.Deadlines, [first, second],
            [new BalanceLinkInput(first.Id, second.Id, "FS", 0)]);

        var (choice, status) = BalanceSolver.Solve(problem, Limit);
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.Equal(BalanceMath.Infeasible, status);
        Assert.Empty(choice);
        Assert.Empty(proposal.Changes);
        Assert.Equal(proposal.Before.LinkViolationDays, proposal.After.LinkViolationDays);
    }

    [Fact]
    public void A_fixed_task_that_cannot_satisfy_an_anchor_is_infeasible_before_solving()
    {
        var running = new BalanceTaskInput(Guid.NewGuid(), "Идёт", "InProgress", null, 16,
            Start.AddDays(4), Start.AddDays(5), null, Role, Anna.Id);
        var successor = Task("Фикс после", 16, Start.AddDays(1), Start.AddDays(2), Boris.Id, datesFixed: true);
        var problem = Problem(BalanceModes.Deadlines, [running, successor],
            [new BalanceLinkInput(running.Id, successor.Id, "FS", 0)]);

        Assert.NotNull(problem.Infeasible);
        var (choice, status) = BalanceSolver.Solve(problem, Limit);

        Assert.Equal(BalanceMath.Infeasible, status);
        Assert.Empty(choice);
    }

    [Fact]
    public void Greedy_fixes_a_link_violation_even_though_the_objective_does_not_price_it()
    {
        var first = Task("Первая", 16, Start.AddDays(4), Start.AddDays(5), Anna.Id);
        var second = Task("Вторая", 16, Start.AddDays(1), Start.AddDays(2), Boris.Id);
        var problem = Problem(BalanceModes.Maximum, [first, second],
            [new BalanceLinkInput(first.Id, second.Id, "FS", 0)]);

        var proposal = BalanceMath.Proposal(problem, BalanceMath.Greedy(problem), BalanceMath.Heuristic);

        Assert.Equal(0, proposal.After.LinkViolationDays);
    }
}
