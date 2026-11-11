using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Keyboard and back navigation in the task list: a row opens both by click and by Enter on the link in
/// the row (the link is keyboard-accessible and leads to the task URL), filters are saved before navigation, and Esc on
/// an open card returns to the list - but does not discard unsaved edits.</summary>
public sealed class TaskListKeysTests : UiTestBase
{
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned" };

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    public TaskListKeysTests()
    {
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer($"tasks/{_task.Id}/history", _ => Array.Empty<HistoryDto>());
    }

    [Fact]
    public void A_row_opens_the_inspector_and_remembers_the_filters()
    {
        var cut = List();
        Field(cut, "Поиск задач").Change("релиз");
        var before = Navigation.Uri;

        Row(cut).Click();

        // The row keeps the tree in place and opens the card in the inspector; the filters and selection are saved.
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<FlareDrawer>().Instance.Open));
        Assert.Contains("Релиз", cut.Find(".flare-drawer").TextContent);
        Assert.Equal(before, Navigation.Uri);

        Navigation.NavigateTo("/tasks");
        var back = Render<Tasks>();
        back.WaitForAssertion(() => Assert.Single(back.FindAll(".flare-datagrid a[href^='tasks/']")));
        Assert.Equal("релиз", Field(back, "Поиск задач").GetAttribute("value"));
        back.WaitForAssertion(() => Assert.True(back.FindComponent<FlareDrawer>().Instance.Open));
    }

    [Fact]
    public void Esc_on_a_clean_card_returns_to_the_list()
    {
        var cut = Card();

        cut.Find("[tabindex='-1'][aria-label]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.EndsWith("/tasks", Navigation.Uri);
    }

    [Fact]
    public void Esc_does_not_throw_unsaved_edits_away()
    {
        var cut = Card();
        var before = Navigation.Uri;
        Field(cut, "Остаток, часы").Change("5");

        cut.Find("[tabindex='-1'][aria-label]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        // The draft holds the card: edits are only sent after explicit confirmation, so no navigation happens.
        Assert.Equal(before, Navigation.Uri);
        Assert.NotEmpty(cut.FindAll("[tabindex='-1'][aria-label]"));
    }

    private IRenderedComponent<Tasks> List()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"a[href='tasks/{_task.Id}']")));
        return cut;
    }

    // Open card: the page renders directly with the task URL, as in the application after navigation.
    private IRenderedComponent<FlareConfirmDialogProvider> Card()
    {
        Navigation.NavigateTo($"/tasks/{_task.Id}");
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<Tasks>(t => t.Add(x => x.TaskId, _task.Id)));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[tabindex='-1'][aria-label]")));
        Assert.Equal(_task.Title, cut.Find("[tabindex='-1'][aria-label]").GetAttribute("aria-label"));
        return cut;
    }

    private AngleSharp.Dom.IElement Row(IRenderedComponent<Tasks> cut) =>
        cut.Find($"a[href='tasks/{_task.Id}']").ParentElement!;
}
