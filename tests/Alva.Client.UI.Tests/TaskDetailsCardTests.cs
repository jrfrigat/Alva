using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Task card: risk with actions, dependencies, unassignment and pinned actions.</summary>
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

    private IRenderedComponent<FlareConfirmDialogProvider> Page(TaskModel? item = null, ActorModel[]? actors = null) =>
        Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, item ?? _task)
            .Add(x => x.Actors, actors ?? [])
            .Add(x => x.Tasks, [item ?? _task, _blocker, _next])));

    private static void ConfirmDialog(IRenderedComponent<FlareConfirmDialogProvider> cut, string action)
    {
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        cut.Find("[role='alertdialog']").QuerySelectorAll("button").First(b => b.TextContent.Trim() == action).Click();
    }

    [Fact]
    public void Actions_are_in_a_sticky_toolbar()
    {
        var toolbar = Page().Find(".alva-task__actions");

        Assert.Contains("Сохранить", toolbar.TextContent);
        Assert.Contains("Назначить исполнителя", toolbar.TextContent);
        Assert.NotNull(toolbar.QuerySelector("[aria-label='Архивировать задачу']"));
    }

    [Fact]
    public void Section_headings_keep_the_icon_and_title_on_the_left()
    {
        var cut = Page();

        foreach (var title in new[] { "Риск", "Описание" })
        {
            var heading = cut.FindAll("h2").Single(h => h.TextContent.Trim() == title);
            Assert.DoesNotContain("flare-stack--stretch-first", heading.ParentElement!.ClassName);
        }
    }

    [Fact]
    public void Title_edit_button_is_in_the_page_header_not_in_the_description_section()
    {
        var cut = Page();

        var edit = cut.Find("[aria-label='Изменить название']");
        Assert.NotNull(edit.Closest(".flare-page-header"));
        var description = cut.FindAll("section").Single(s => s.QuerySelector("h2")?.TextContent.Trim() == "Описание");
        Assert.Null(description.QuerySelector("[aria-label='Изменить название']"));
    }

    [Fact]
    public void Title_dialog_cancel_keeps_the_title_and_apply_changes_the_draft()
    {
        var cut = Page();
        var oldTitle = _task.Title;

        cut.Find("[aria-label='Изменить название']").Click();
        Field(cut, "Название").Change("Новое название");
        var dialog = cut.FindAll("[role=dialog]").Single(d => d.TextContent.Contains("Название задачи"));
        dialog.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Отмена").Click();
        Assert.Equal(oldTitle, cut.Find("h1").TextContent.Trim());

        cut.Find("[aria-label='Изменить название']").Click();
        Field(cut, "Название").Change("Новое название");
        dialog = cut.FindAll("[role=dialog]").Single(d => d.TextContent.Contains("Название задачи"));
        dialog.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Применить").Click();
        Assert.Equal("Новое название", cut.Find("h1").TextContent.Trim());
        Assert.False(Button(cut, "Сохранить").HasAttribute("disabled"));
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
        Assert.DoesNotContain(block.QuerySelectorAll("a"), a => a.GetAttribute("href") == $"tasks/{_task.Id}");
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
    public void Forecast_note_is_shown_without_a_risk_level()
    {
        Api.Answer("risks", _ => new[]
        {
            new RiskDto(_task.Id, _task.Title, "Info", [], null, [new ReasonDto(ReasonCodes.ForecastUnknownRemaining)])
        });

        var block = Page().Find(".alva-task__risk");

        Assert.Contains("Рисков нет", block.TextContent);
        Assert.Contains("Неизвестен остаток работы", block.TextContent);
        Assert.DoesNotContain("Средний", block.TextContent);
    }

    [Fact]
    public void Plan_fields_are_grouped_and_dates_stay_together()
    {
        var cut = Page();

        var plan = cut.FindAll("section").Single(s => s.QuerySelector("h2")?.TextContent.Trim() == "План");
        Assert.Contains("Контекст", plan.TextContent);
        Assert.Contains("Усилие", plan.TextContent);
        Assert.Contains("Даты", plan.TextContent);
        Assert.Same(Field(cut, "Начало").Closest(".flare-grid"), Field(cut, "Окончание").Closest(".flare-grid"));
        Assert.Contains("flex:1 1 16rem", cut.Markup);
    }

    [Fact]
    public void Subtask_rows_do_not_repeat_the_skill_match()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "Backend", Kind = "Skill" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person", SkillIds = [skill.Id] };
        var parent = new TaskModel { Id = _task.Id, Title = "Родитель", Status = "Planned", Version = 1 };
        var child = new TaskModel { Id = Guid.NewGuid(), Title = "Дочерняя", ParentId = parent.Id, Status = "Planned", Version = 1, AssignmentTargetId = person.Id, RequiredSkillIds = [skill.Id] };
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, parent)
            .Add(x => x.Actors, [skill, person])
            .Add(x => x.Tasks, [parent, child])));

        var subtasks = cut.FindAll("section").Single(s => s.QuerySelector("h2")?.TextContent.Trim() == "Подзадачи");
        Assert.Contains("Анна", subtasks.TextContent);
        Assert.DoesNotContain("Все необходимые навыки есть", subtasks.TextContent);
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

        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача"), $"{AlvaFormat.Code(_next.Id)} {_next.Title}");
        Button(cut, "Дополнительно").Click();
        Choose(cut.FindComponent<FlareSelect<bool>>(), "Последователь — ждёт эту задачу");
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
    public void Assignment_keeps_the_draft_version_in_sync()
    {
        var actor = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var item = new TaskModel { Id = _task.Id, Title = _task.Title, Status = _task.Status, Version = 1, AssignmentTargetId = actor.Id };
        Api.SaveResult = new TaskModel { Id = _task.Id, Title = _task.Title, Status = _task.Status, Version = 5 };
        var cut = Page(item, [actor]);

        Field(cut, "Описание и цели").Change("Черновик");
        Button(cut, "Назначить исполнителя").Click();
        cut.WaitForAssertion(() => Assert.Contains("Применить назначение", cut.Markup));
        Button(cut, "Применить назначение").Click();
        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put && s.Path.EndsWith("/assignment")));

        Button(cut, "Сохранить").Click();
        cut.WaitForAssertion(() => Assert.Contains(Api.Sent,
            s => s.Method == HttpMethod.Put && s.Path.EndsWith($"tasks/{_task.Id}") && ((TaskRequest)s.Value).Version == 5));
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
    public void Assignment_candidates_come_from_the_nested_departments_of_the_target()
    {
        var root = new ActorModel { Id = Guid.NewGuid(), Name = "Разработка", Kind = "Department" };
        var child = new ActorModel { Id = Guid.NewGuid(), Name = "Платформа", Kind = "Department", ParentId = root.Id };
        var other = new ActorModel { Id = Guid.NewGuid(), Name = "Продажи", Kind = "Department" };
        var member = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person", DepartmentIds = [child.Id] };
        var outsider = new ActorModel { Id = Guid.NewGuid(), Name = "Борис", Kind = "Person", DepartmentIds = [other.Id] };
        var item = new TaskModel { Id = _task.Id, Title = _task.Title, Status = "Planned", Version = 1, AssignmentTargetId = root.Id };
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d
            .Add(x => x.Item, item)
            .Add(x => x.Actors, [root, child, other, member, outsider])
            .Add(x => x.Tasks, [item])));

        Button(cut, "Назначить исполнителя").Click();
        cut.WaitForAssertion(() => Assert.Contains("Снять назначение", cut.Markup));
        cut.FindAll("button").First(button => button.TextContent.Contains("Человек", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("Анна", cut.Markup));
        Assert.DoesNotContain("Борис", cut.Markup);
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
