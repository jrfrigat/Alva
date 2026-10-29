using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Заведомо неверная форма не уходит в API, а ошибка стоит у поля.</summary>
public sealed class ValidationTests : UiTestBase
{
    [Fact]
    public void A_task_with_the_end_before_the_start_is_not_saved()
    {
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };
        Api.Answer($"tasks/{task.Id}/history", _ => Array.Empty<HistoryDto>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, task)
            .Add(x => x.Tasks, new[] { task })));

        PickDate(cut, "Начало", new DateOnly(2026, 10, 20));
        PickDate(cut, "Окончание", new DateOnly(2026, 10, 10));
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("Окончание раньше начала.", cut.Markup));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Put);
    }

    [Fact]
    public void A_task_cannot_become_its_own_descendant()
    {
        var parent = new TaskModel { Id = Guid.NewGuid(), Title = "Этап", Status = "Planned", Version = 1 };
        var child = new TaskModel { Id = Guid.NewGuid(), Title = "Работа", ParentId = parent.Id, Status = "Planned", Version = 1 };
        Api.Answer($"tasks/{parent.Id}/history", _ => Array.Empty<HistoryDto>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, parent)
            .Add(x => x.Tasks, new[] { parent, child })));

        var select = cut.FindComponents<FlareSelect<Guid?>>().First(s => s.Instance.Label == "Родительская задача");
        cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(child.Id));
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("Родительская задача не может быть самой задачей или её потомком.", cut.Markup));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Put);
    }

    [Fact]
    public void A_sprint_with_the_end_before_the_start_is_not_saved()
    {
        Api.Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Provider<Sprints>();

        Button(cut, "Новый спринт").Click();
        Field(cut, "Название").Change("Спринт");
        PickDate(cut, "Начало", new DateOnly(2026, 10, 20));
        PickDate(cut, "Окончание", new DateOnly(2026, 10, 10));
        Button(cut, "Сохранить спринт").Click();

        cut.WaitForAssertion(() => Assert.Contains("Окончание раньше начала.", cut.Markup));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Post || s.Method == HttpMethod.Put);
    }

    [Fact]
    public void A_member_with_an_invalid_email_is_not_saved()
    {
        Api.Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Provider<Alva.Client.Pages.Directory>();

        Button(cut, "Добавить").Click();
        Field(cut, "Имя или название").Change("Анна");
        Field(cut, "Электронная почта").Change("не-почта");
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("Некорректный адрес почты.", cut.Markup));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Post || s.Method == HttpMethod.Put);
    }

    [Fact]
    public void An_unknown_time_zone_is_not_saved()
    {
        var person = Guid.NewGuid();
        var calendar = new CalendarDto(person, "Europe/Moscow", 8, 8, 8, 8, 8, 0, 0, [], []);
        Api.Answer("actors", _ => new[] { new ActorModel { Id = person, Name = "Анна" } })
           .Answer($"actors/{person}/calendar", _ => calendar)
           .Answer("capacity", _ => Array.Empty<CapacityDto>());
        var cut = Render<ScheduleEditor>(p => p.Add(x => x.ActorId, person));

        Field(cut, "Часовой пояс IANA").Change("Mars/Olympus");
        Button(cut, "Сохранить график").Click();

        cut.WaitForAssertion(() => Assert.Contains("Неизвестный часовой пояс.", cut.Markup));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Put);
    }

    private static void PickDate(IRenderedComponent<FlareConfirmDialogProvider> cut, string label, DateOnly value)
    {
        var picker = cut.FindComponents<FlareDatePicker>().First(p => p.Instance.Label == label);
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(value));
    }

    private IRenderedComponent<FlareConfirmDialogProvider> Provider<T>() where T : Microsoft.AspNetCore.Components.IComponent =>
        Render<FlareConfirmDialogProvider>(p => p.AddChildContent<T>());
}
