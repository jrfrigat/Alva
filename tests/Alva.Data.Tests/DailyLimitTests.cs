using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Лимит часов в день: распределение, прогноз и автобаланс держат задачу в договорённых часах.</summary>
public sealed class DailyLimitTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 10, 5);

    [Fact]
    public void Spread_caps_each_day_and_carries_the_rest_past_the_finish()
    {
        var calendar = CapacityMath.DefaultCalendar(Person);

        var days = CapacityMath.Spread(calendar, 10m, Monday, Monday.AddDays(2), 2m).ToArray();

        Assert.All(days, d => Assert.True(d.Hours <= 2m));
        Assert.Equal(10m, days.Sum(d => d.Hours));
        Assert.Equal(Monday.AddDays(4), days[^1].Day);
        Assert.Equal(Monday.AddDays(4), CapacityMath.Finish(calendar, 10m, Monday, Monday.AddDays(2), 2m));
    }

    [Fact]
    public void Spread_without_a_limit_is_unchanged()
    {
        var days = CapacityMath.Spread(CapacityMath.DefaultCalendar(Person), 10m, Monday, Monday.AddDays(1)).ToArray();

        Assert.Equal([5m, 5m], days.Select(d => d.Hours));
    }

    [Fact]
    public void Forecast_takes_no_more_than_the_limit_a_day()
    {
        var id = Guid.NewGuid();
        var result = ForecastMath.Build([new ForecastWork(id, null, Person, "Planned", 10m, null, Monday, null, null, DailyHoursLimit: 2m)],
            [], CapacityMath.DefaultCalendar, Monday);

        Assert.Equal(Monday.AddDays(4), result[id].Finish);
    }

    [Fact]
    public void Risk_warns_when_the_limit_pushes_work_past_the_window()
    {
        var task = new RiskTask(Guid.NewGuid(), null, "Отчёт", 10m, Monday, Monday.AddDays(1), null, Person, "Анна", null, DailyHoursLimit: 2m);

        var risk = Assert.Single(RiskMath.Assess([task], [], CapacityMath.DefaultCalendar, Monday));

        Assert.Contains(risk.Reasons, r => r.Code == ReasonCodes.RiskDailyLimit && r.Args![1] == "2026-10-09");
    }

    [Fact]
    public void Balance_stretches_a_movable_task_and_keeps_a_fixed_tail_for_links()
    {
        var role = Guid.NewGuid();
        var anna = new BalancePersonInput(Person, "Анна", [role], []);
        var report = new BalanceTaskInput(Guid.NewGuid(), "Отчёт", "Planned", null, 10m, Monday, Monday.AddDays(1), null, Person, Person,
            DatesFixed: true, DailyHoursLimit: 2m);
        var next = new BalanceTaskInput(Guid.NewGuid(), "Анонс", "Planned", null, 4m, Monday.AddDays(2), Monday.AddDays(2), null, Person, Person);
        var problem = BalanceMath.Build(BalanceModes.Deadlines, Guid.NewGuid(), Monday, Monday.AddDays(11), [anna], CapacityMath.DefaultCalendar,
            [report, next], [new BalanceLinkInput(report.Id, next.Id, "FS", 0)]);

        var fixedOption = problem.Tasks.Single(t => t.Id == report.Id).Options[0];
        Assert.Equal(2, fixedOption.Length);
        Assert.Equal(3, fixedOption.Tail);
        Assert.All(fixedOption.Load, l => Assert.True(l <= 20));

        var choice = BalanceMath.Greedy(problem);
        var successor = problem.Tasks.Single(t => t.Id == next.Id);
        Assert.True(successor.Options[choice[Array.IndexOf(problem.Tasks, successor)]].Start >= 5);

        var movable = BalanceMath.Build(BalanceModes.Deadlines, Guid.NewGuid(), Monday, Monday.AddDays(11), [anna], CapacityMath.DefaultCalendar,
            [report with { DatesFixed = false }], []);
        var proposal = BalanceMath.Proposal(movable, new int[1], BalanceMath.Heuristic);
        var change = Assert.Single(proposal.Changes);
        Assert.Equal(Monday.AddDays(4), change.ToEnd);
        Assert.Contains(change.Reasons, r => r.Code == ReasonCodes.BalanceDailyLimit);
    }

    [Fact]
    public void Solver_keeps_the_successor_after_the_real_finish_and_the_day_under_the_limit()
    {
        var role = Guid.NewGuid();
        var anna = new BalancePersonInput(Person, "Анна", [role], []);
        var report = new BalanceTaskInput(Guid.NewGuid(), "Отчёт", "Planned", null, 10m, Monday, Monday.AddDays(1), null, Person, Person, DailyHoursLimit: 2m);
        var next = new BalanceTaskInput(Guid.NewGuid(), "Анонс", "Planned", null, 4m, Monday.AddDays(2), Monday.AddDays(2), null, Person, Person);
        var problem = BalanceMath.Build(BalanceModes.Deadlines, Guid.NewGuid(), Monday, Monday.AddDays(11), [anna], CapacityMath.DefaultCalendar,
            [report, next], [new BalanceLinkInput(report.Id, next.Id, "FS", 0)]);

        var (choice, status) = Alva.Api.BalanceSolver.Solve(problem, TimeSpan.FromSeconds(5));
        var proposal = BalanceMath.Proposal(problem, choice, status);

        Assert.NotEqual(BalanceMath.Infeasible, status);
        Assert.Equal(0, proposal.After.LinkViolationDays);
        var r = Array.FindIndex(problem.Tasks, t => t.Id == report.Id);
        var n = Array.FindIndex(problem.Tasks, t => t.Id == next.Id);
        var chosen = problem.Tasks[r].Options[choice[r]];
        Assert.All(chosen.Load, l => Assert.True(l <= 20));
        Assert.True(problem.Tasks[n].Options[choice[n]].Start >= chosen.Start + chosen.Span);
        Assert.Contains(proposal.Changes, c => c.TaskId == report.Id && c.ToEnd > Monday.AddDays(1));
    }
}
