using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Creation stays in the tree: a quick composer keeps the context, the row and the focus.</summary>
public sealed class TasksQuickCreateTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Портал" };
    private readonly TaskModel _parent = new() { Id = Guid.NewGuid(), Title = "Планирование", Status = "Planned", ProjectId = default };
    private readonly Guid _savedId = Guid.NewGuid();

    public TasksQuickCreateTests()
    {
        _parent.ProjectId = _project.Id;
        Api.Answer("tasks", _ => new[] { _parent })
           .Answer("projects", _ => new[] { _project })
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
        Api.SaveResult = new TaskModel { Id = _savedId, Title = "Новая", Status = "Backlog", ProjectId = _project.Id };
    }

    [Fact]
    public void Full_create_form_uses_the_shared_plan_form()
    {
        var cut = Page();

        Button(cut, "Новая задача").Click();
        Button(cut, "Больше полей").Click();

        Assert.NotEmpty(cut.FindComponents<TaskFormPlan>());
        Assert.DoesNotContain(cut.FindAll("label"), l => l.TextContent.Trim() == "Спринт (по датам)");
    }

    [Fact]
    public void More_fields_keeps_one_drawer_and_carries_the_typed_title()
    {
        var cut = Page();

        Button(cut, "Новая задача").Click();
        Field(cut, "Название").Change("Черновик");
        Button(cut, "Больше полей").Click();

        var drawer = cut.FindComponent<FlareDrawer>();
        Assert.True(drawer.Instance.Open);
        Assert.Equal(DrawerAnchor.Right, drawer.Instance.Anchor);
        Assert.NotEmpty(cut.FindComponents<TaskFormPlan>());
        Assert.Equal("Черновик", Field(cut, "Название").GetAttribute("value"));
    }

    [Fact]
    public void Adding_a_subtask_switches_the_same_right_drawer_instead_of_opening_another()
    {
        var cut = Page();
        cut.FindAll(".flare-datagrid__row").First(n => n.TextContent.Contains("Планирование")).Click();
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<FlareDrawer>().Instance.Open));
        var viewDrawer = cut.FindComponent<FlareDrawer>();

        cut.Find("[aria-label='Добавить подзадачу']").Click();

        var drawer = cut.FindComponent<FlareDrawer>();
        Assert.Same(viewDrawer.Instance, drawer.Instance);
        Assert.Equal(DrawerAnchor.Right, drawer.Instance.Anchor);
        Assert.True(drawer.Instance.Open);
        Assert.DoesNotContain(cut.FindComponents<FlareDrawer>(), d => d.Instance.Anchor == DrawerAnchor.Bottom);
        Assert.Contains("Планирование", drawer.Markup);
        Assert.NotEmpty(cut.FindComponents<TaskFormPlan>());
        Assert.Contains("Сохранить задачу", drawer.Find(".flare-drawer").TextContent);
    }

    [Fact]
    public void Assignment_failure_does_not_create_a_second_task()
    {
        var anna = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        Api.Answer("actors", _ => new[] { anna });
        var saved = new TaskModel { Id = _savedId, Title = "Новая", Status = "Backlog", Version = 1 };
        Api.SaveResult = saved;
        var failAssignment = true;
        Api.OnSend = (method, path, _) => failAssignment && method == HttpMethod.Put && path.EndsWith("/assignment")
            ? new HttpRequestException("assignment failed") : null;

        var cut = Page();
        Button(cut, "Новая задача").Click();
        Button(cut, "Больше полей").Click();
        Field(cut, "Название").Change("Новая");
        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Исполнитель: человек или подкоманда"), "Анна - Человек");
        Button(cut, "Сохранить задачу").Click();

        cut.WaitForAssertion(() => Assert.Single(Api.Sent, s => s.Method == HttpMethod.Post));
        failAssignment = false;
        Button(cut, "Сохранить задачу").Click();

        cut.WaitForAssertion(() => Assert.Single(Api.Sent, s => s.Method == HttpMethod.Post));
        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put && s.Path.EndsWith($"tasks/{_savedId}")));
    }

    private static void Choose<T>(IRenderedComponent<FlareSelect<T>> select, string option)
    {
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(e => e.TextContent.Trim() == option).Click();
    }

    private IRenderedComponent<Tasks> Page()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-datagrid__row")));
        return cut;
    }

    [Fact]
    public void Quick_create_posts_once_and_keeps_the_tree_with_the_new_row()
    {
        var cut = Page();
        var url = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().Uri;

        Button(cut, "Новая задача").Click();
        Field(cut, "Название").Change("Новая");
        Button(cut, "Создать").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Post && s.Value is TaskRequest r && r.Title == "Новая"));
        cut.WaitForAssertion(() => Assert.Contains("alva-task-row--new", cut.Markup));
        Assert.Contains("Новая", cut.Markup);
        Assert.Equal(url, Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().Uri);
    }

    [Fact]
    public void Adding_a_subtask_opens_the_full_form_with_the_parent()
    {
        var cut = Page();

        cut.Find("[aria-label='Добавить подзадачу']").Click();

        Assert.NotEmpty(cut.FindComponents<TaskFormPlan>());
        var parent = cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Родительская задача");
        Assert.Equal(_parent.Id, parent.Instance.Value);
    }

    [Fact]
    public void Create_another_keeps_the_composer_open_and_clears_the_title()
    {
        var cut = Page();

        Button(cut, "Новая задача").Click();
        Field(cut, "Название").Change("Первая");
        Button(cut, "Создать ещё").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Post && s.Value is TaskRequest r && r.Title == "Первая"));
        var drawer = cut.FindComponents<FlareDrawer>().Single(d => d.Instance.AriaLabel == "Новая задача");
        Assert.True(drawer.Instance.Open);
        Assert.Equal("", Field(cut, "Название").GetAttribute("value"));
    }
}
