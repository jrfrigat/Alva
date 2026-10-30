using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Карточка задачи: риск с действиями, зависимости, снятие назначения и закреплённые действия.</summary>
public sealed class TaskDetailsCardTests : UiTestBase
{
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };
    private readonly TaskModel _blocker = new() { Id = Guid.NewGuid(), Title = "Миграция", Status = "InProgress", Version = 1 };
    private readonly TaskModel _next = new() { Id = Guid.NewGuid(), Title = "Анонс", Status = "Planned", Version = 1 };

    public TaskDetailsCardTests()
    {
        Api.Answer("tasks/", _ => Array.Empty<HistoryDto>())
           .Answer("capacity", _ => Array.Empty<CapacityDto>());
    }

    private IRenderedComponent<FlareConfirmDialogProvider> Page(TaskModel? item = null) =>
        Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, item ?? _task)
            .Add(x => x.Tasks, [item ?? _task, _blocker, _next])));

    private static void ConfirmDialog(IRenderedComponent<FlareConfirmDialogProvider> cut, string action)
    {
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        cut.Find("[role='alertdialog']").QuerySelectorAll("button").First(b => b.TextContent.Trim() == action).Click();
    }

    [Fact]
    public void Actions_stay_in_a_labelled_sticky_toolbar()
    {
        var toolbar = Page().Find("[role='toolbar']");

        Assert.Contains("alva-task__actions", toolbar.ClassName);
        Assert.Equal("Действия с задачей", toolbar.GetAttribute("aria-label"));
        Assert.Contains("Сохранить", toolbar.TextContent);
        Assert.Contains("Назначить исполнителя", toolbar.TextContent);
    }

    [Fact]
    public void Risk_block_explains_reasons_and_links_to_the_blocker()
    {
        Api.Answer("risks", _ => new[]
        {
            new RiskDto(_task.Id, _task.Title, "High",
                [new ReasonDto(ReasonCodes.RiskOverdue), new ReasonDto(ReasonCodes.RiskDependencyOpen, [_blocker.Id.ToString()])], null)
        });

        var cut = Page();

        var block = cut.Find(".alva-task__risk");
        Assert.Contains("Высокий", block.TextContent);
        Assert.Contains("Срок прошёл", block.TextContent);
        Assert.Contains(block.QuerySelectorAll("a"), a => a.GetAttribute("href") == $"tasks/{_blocker.Id}");
    }

    [Fact]
    public void Missing_assignee_reason_opens_the_assignment_panel()
    {
        Api.Answer("risks", _ => new[] { new RiskDto(_task.Id, _task.Title, "Medium", [new ReasonDto(ReasonCodes.RiskNoAssignee)], null) });
        var cut = Page();

        cut.Find(".alva-task__risk").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Reads, r => r.Contains("capacity")));
    }

    [Fact]
    public void Task_without_risk_says_so()
    {
        Assert.Contains("Рисков нет", Page().Find(".alva-task__risk").TextContent);
    }

    [Fact]
    public void Dependencies_list_predecessors_and_successors_with_type_and_lag()
    {
        Api.Answer($"tasks/{_task.Id}/dependencies", _ => new[]
        {
            new DependencyDto(Guid.NewGuid(), _blocker.Id, _task.Id, "FS", 2),
            new DependencyDto(Guid.NewGuid(), _task.Id, _next.Id, "SS", 0)
        });

        var deps = Page().Find(".alva-task__deps");

        Assert.Contains("Предшественники (1)", deps.TextContent);
        Assert.Contains("Последователи (1)", deps.TextContent);
        Assert.Contains("Окончание → начало · лаг 2 дн.", deps.TextContent);
        Assert.Contains("Начало → начало", deps.TextContent);
        Assert.Contains(deps.QuerySelectorAll("a"), a => a.GetAttribute("href") == $"tasks/{_next.Id}");
    }

    [Fact]
    public void Removing_a_dependency_asks_first_and_then_deletes_it()
    {
        var link = new DependencyDto(Guid.NewGuid(), _blocker.Id, _task.Id, "FS", 0);
        Api.Answer($"tasks/{_task.Id}/dependencies", _ => new[] { link });
        var cut = Page();

        cut.Find("[aria-label='Удалить связь с «Миграция»']").Click();
        ConfirmDialog(cut, "Удалить");

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Delete && s.Path.EndsWith($"tasks/dependencies/{link.Id}")));
    }

    [Fact]
    public void Removing_a_dependency_can_be_cancelled()
    {
        Api.Answer($"tasks/{_task.Id}/dependencies", _ => new[] { new DependencyDto(Guid.NewGuid(), _blocker.Id, _task.Id, "FS", 0) });
        var cut = Page();

        cut.Find("[aria-label='Удалить связь с «Миграция»']").Click();
        ConfirmDialog(cut, "Отмена");

        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Delete);
    }

    [Fact]
    public void Added_dependency_goes_to_the_server_in_the_chosen_direction()
    {
        var cut = Page();

        Choose(cut.FindComponent<FlareSelect<bool>>(), "Последователь — ждёт эту задачу");
        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача"), "Анонс");
        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Вид"), "Начало → начало");
        Button(cut, "Добавить связь").Click();

        cut.WaitForAssertion(() =>
        {
            var sent = Assert.Single(Api.Sent, s => s.Method == HttpMethod.Post);
            Assert.EndsWith($"tasks/{_task.Id}/dependencies", sent.Path);
            Assert.Equal(new DependencyRequest(_task.Id, _next.Id, "SS", 0), sent.Value);
        });
    }

    [Fact]
    public void Assignment_can_be_removed_and_focus_returns_to_the_assign_button()
    {
        var assigned = new TaskModel { Id = _task.Id, Title = _task.Title, Status = "Planned", Version = 1, AssignmentTargetId = Guid.NewGuid() };
        var cut = Page(assigned);

        Button(cut, "Назначить исполнителя").Click();
        cut.WaitForAssertion(() => Assert.Contains("Снять назначение", cut.Markup));
        var focusesBefore = Focuses();
        Button(cut, "Снять назначение").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Delete && s.Path.EndsWith($"tasks/{_task.Id}/assignment")));
        cut.WaitForAssertion(() => Assert.True(Focuses() > focusesBefore));
        Assert.False(cut.FindComponent<FlareDrawer>().Instance.Open);
    }

    [Fact]
    public void Unassigned_task_offers_no_unassign_button()
    {
        var cut = Page();

        Button(cut, "Назначить исполнителя").Click();

        cut.WaitForAssertion(() => Assert.Contains("Применить назначение", cut.Markup));
        Assert.DoesNotContain("Снять назначение", cut.Markup);
    }

    [Fact]
    public void Project_breadcrumb_opens_the_selected_project()
    {
        var project = new ProjectModel { Id = Guid.NewGuid(), Name = "Портал" };
        var item = new TaskModel { Id = _task.Id, Title = _task.Title, Status = "Planned", Version = 1, ProjectId = project.Id };
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, item).Add(x => x.Projects, [project])));

        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == $"projects?project={project.Id}");
    }

    [Fact]
    public void Archive_confirmation_does_not_promise_a_restore_the_app_lacks()
    {
        var cut = Page();

        cut.Find("[aria-label='Архивировать задачу']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        var text = cut.Find("[role='alertdialog']").TextContent;
        Assert.Contains("Вернуть её из приложения пока нельзя", text);
        Assert.DoesNotContain("восстановить", text);
    }

    private int Focuses() => JSInterop.Invocations.Count(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

    private static void Choose<T>(IRenderedComponent<FlareSelect<T>> select, string option)
    {
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(e => e.TextContent.Trim() == option).Click();
    }
}
