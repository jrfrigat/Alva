using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Filtering keeps the ancestors of a match and shows what is active.</summary>
public sealed class TasksFilterHierarchyTests : UiTestBase
{
    private readonly TaskModel _parent = new() { Id = Guid.NewGuid(), Title = "Планирование", Status = "Planned", Order = 0 };
    private readonly TaskModel _child = new() { Id = Guid.NewGuid(), Title = "API планирования", Status = "Planned", Order = 0 };
    private readonly TaskModel _other = new() { Id = Guid.NewGuid(), Title = "Отчёт", Status = "Backlog", Order = 1 };
    private readonly ActorModel _anna = new() { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
    private readonly ActorModel _petr = new() { Id = Guid.NewGuid(), Name = "Пётр", Kind = "Person" };

    public TasksFilterHierarchyTests()
    {
        _child.ParentId = _parent.Id;
        Api.Answer("tasks", _ => new[] { _parent, _child, _other })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => new[] { _anna, _petr })
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private IRenderedComponent<Tasks> Page()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-tree__row").Count));
        return cut;
    }

    [Fact]
    public void A_search_keeps_the_parent_and_expands_the_path_to_the_match()
    {
        var cut = Page();

        Field(cut, "Поиск задач").Input("API");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-tree__row").Count));
        var rows = cut.FindAll(".alva-tree__row");
        Assert.Contains("Планирование", rows[0].TextContent);
        Assert.Contains("API планирования", rows[1].TextContent);
        Assert.Contains("alva-tree__row--context", rows[0].ClassName);
        Assert.Contains("Поиск: API", cut.Markup);
        Assert.Contains("Найдено: 1", cut.Markup);
    }

    [Fact]
    public void An_empty_result_explains_itself_and_clears_the_filters()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Исполнитель"), "Пётр - Человек");

        cut.WaitForAssertion(() => Assert.Contains("Нет задач по условиям", cut.Markup));
        Assert.Contains("Сбросить фильтры", cut.Markup);

        Button(cut, "Сбросить фильтры").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-tree__row").Count));
        Assert.DoesNotContain("Поиск:", cut.Markup);
    }

    private static void Choose<T>(IRenderedComponent<FlareSelect<T>> select, string option)
    {
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(e => e.TextContent.Trim() == option).Click();
    }
}
