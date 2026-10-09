using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

public sealed class RiskMathTests
{
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Monday = Friday.AddDays(3);

    private static CalendarDto Week(params CalendarExceptionDto[] exceptions) =>
        new(Anna, "Europe/Moscow", 8m, 8m, 8m, 8m, 8m, 0m, 0m, exceptions, []);

    private static RiskTask Task(string title, decimal? hours, DateOnly? start, DateOnly? end, DateOnly? deadline, bool assigned = true, Guid? parent = null, string? role = null) =>
        new(Guid.NewGuid(), parent, title, hours, start, end, deadline, assigned ? Anna : null, assigned ? "Анна" : null, role);

    private static RiskDto[] Assess(CalendarDto calendar, params RiskTask[] tasks) => RiskMath.Assess(tasks, [], _ => calendar, Friday);

    [Fact]
    public void Missing_people_are_named_by_assignment()
    {
        var risks = Assess(Week(), Task("Без назначения", 4m, Monday, Monday, null, assigned: false), Task("На роль", 4m, Monday, Monday, null, assigned: false, role: "Backend"));

        Assert.Equal(["Нет исполнителя"], risks.Single(x => x.Title == "Без назначения").Reasons);
        Assert.Equal(["Требуется человек: Backend"], risks.Single(x => x.Title == "На роль").Reasons);
    }

    [Fact]
    public void Passed_deadline_is_high()
    {
        var risk = Assert.Single(Assess(Week(), Task("Просрочена", 4m, null, null, Friday.AddDays(-1))));

        Assert.Equal("High", risk.Severity);
        Assert.Contains("Срок прошёл", risk.Reasons);
    }

    [Fact]
    public void Forecast_follows_the_assignee_calendar_through_a_vacation()
    {
        var vacation = new CalendarExceptionDto(Guid.NewGuid(), Monday, Monday.AddDays(1), 0m, "Отпуск");
        var risk = Assert.Single(Assess(Week(vacation), Task("Отчёт", 16m, Monday, Monday.AddDays(2), Monday.AddDays(2))));

        Assert.Equal(Monday.AddDays(3), risk.ForecastDate);
        Assert.Equal("Medium", risk.Severity);
        // Отпуск сжимает окно до среды: 16 ч на 8-часовой день — это и есть перегрузка.
        Assert.Equal(["Прогноз позже срока", "Исполнитель недоступен: Отпуск (12.10–13.10)", "Перегрузка исполнителя: Анна"], risk.Reasons);
    }

    [Fact]
    public void Past_absence_does_not_count()
    {
        var past = new CalendarExceptionDto(Guid.NewGuid(), Friday.AddDays(-7), Friday.AddDays(-5), 0m, "Отпуск");

        Assert.Empty(Assess(Week(past), Task("Идёт", 8m, Friday.AddDays(-7), Monday, Monday.AddDays(5))));
    }

    [Fact]
    public void Overload_is_found_in_the_task_window_only()
    {
        var risks = Assess(Week(), Task("Первая", 8m, Monday, Monday, null), Task("Вторая", 8m, Monday, Monday, null), Task("Свободная", 8m, Monday.AddDays(1), Monday.AddDays(1), null));

        Assert.Equal(["Вторая", "Первая"], risks.Select(x => x.Title).Order());
        Assert.All(risks, x => Assert.Equal(["Перегрузка исполнителя: Анна"], x.Reasons));
    }

    [Fact]
    public void Parent_takes_estimate_and_deadline_from_children()
    {
        var parent = Task("Этап", null, Monday, Monday.AddDays(4), null);
        var child = Task("Работа", 24m, Monday, Monday.AddDays(4), Monday.AddDays(1), assigned: false, parent: parent.Id) with { PersonId = Guid.NewGuid(), PersonName = "Пётр" };
        var risks = RiskMath.Assess([parent, child], [], _ => Week(), Friday);

        var stage = risks.Single(x => x.TaskId == parent.Id);
        Assert.Equal(Monday.AddDays(2), stage.ForecastDate);
        Assert.Contains("Прогноз позже срока", stage.Reasons);
    }

    [Fact]
    public void Open_predecessor_blocks_its_successor()
    {
        var first = Task("Сначала", 4m, Monday, Monday, null);
        var second = Task("Потом", 4m, Monday.AddDays(1), Monday.AddDays(1), null);
        var risks = RiskMath.Assess([first, second], [new RiskDependency(first.Id, second.Id), new RiskDependency(Guid.NewGuid(), first.Id)], _ => Week(), Friday);

        Assert.Equal(["Зависимость не завершена"], Assert.Single(risks).Reasons);
    }

    [Fact]
    public void High_risks_come_first()
    {
        var risks = Assess(Week(), Task("Б средний", 4m, Monday, Monday, null, assigned: false), Task("А высокий", 4m, null, null, Friday.AddDays(-2)));

        Assert.Equal(["А высокий", "Б средний"], risks.Select(x => x.Title));
    }

    [Fact]
    public void Forecast_without_hours_is_unknown()
    {
        var never = new CalendarDto(Anna, "Europe/Moscow", 0m, 0m, 0m, 0m, 0m, 0m, 0m, [], []);

        Assert.Null(RiskMath.Forecast(never, 8m, Friday));
        Assert.Equal(Friday, RiskMath.Forecast(never, 0m, Friday));
    }
}
