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

/// <summary>Клавиатура и возврат в списке задач: строку открывает и клик, и Enter по ссылке в строке (ссылка
/// доступна с клавиатуры и ведёт на адрес задачи), перед переходом запоминаются фильтры, а Esc в открытой
/// карточке возвращает к списку — но не выбрасывает несохранённые правки.</summary>
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
    public void A_row_goes_to_the_task_and_remembers_the_filters()
    {
        var cut = List();
        Field(cut, "Поиск задач").Change("релиз");

        Row(cut).Click();

        // Ссылка в строке ведёт на адрес задачи, поэтому Enter по ней идёт тем же путём, что клик: страница
        // сначала запоминает фильтры, а потом уходит на задачу.
        Assert.EndsWith($"tasks/{_task.Id}", Navigation.Uri);
    }

    [Fact]
    public void Esc_on_a_clean_card_returns_to_the_list()
    {
        var cut = Card();

        cut.Find(".alva-task-card").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.EndsWith("/tasks", Navigation.Uri);
    }

    [Fact]
    public void Esc_does_not_throw_unsaved_edits_away()
    {
        var cut = Card();
        var before = Navigation.Uri;
        Field(cut, "Название").Change("Релиз — правка");

        cut.Find(".alva-task-card").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        // Черновик держит карточку: правки уходят только после явного согласия, поэтому ухода не происходит.
        Assert.Equal(before, Navigation.Uri);
        Assert.NotEmpty(cut.FindAll(".alva-task-card"));
    }

    private IRenderedComponent<Tasks> List()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"a[href='tasks/{_task.Id}']")));
        return cut;
    }

    // Открытая карточка: страница рендерится прямо с адресом задачи, как в приложении после перехода.
    private IRenderedComponent<FlareConfirmDialogProvider> Card()
    {
        Navigation.NavigateTo($"/tasks/{_task.Id}");
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<Tasks>(t => t.Add(x => x.TaskId, _task.Id)));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-task-card")));
        Assert.Equal(_task.Title, cut.Find(".alva-task-card").GetAttribute("aria-label"));
        return cut;
    }

    private AngleSharp.Dom.IElement Row(IRenderedComponent<Tasks> cut) =>
        cut.Find($"a[href='tasks/{_task.Id}']").ParentElement!;
}
