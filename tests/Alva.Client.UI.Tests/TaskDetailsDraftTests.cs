using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Bunit.TestDoubles;
using Flare.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Несохранённые правки карточки задачи не теряются молча при уходе со страницы.</summary>
public sealed class TaskDetailsDraftTests : UiTestBase
{
    private TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };

    public TaskDetailsDraftTests()
    {
        Api.Answer("tasks/", _ => Array.Empty<HistoryDto>());
        Navigation.NavigateTo($"/tasks/{_task.Id}");
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    // Как в приложении: карточка внутри провайдера подтверждений из MainLayout.
    private IRenderedComponent<FlareConfirmDialogProvider> Page() =>
        Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d.Add(x => x.Item, _task)));

    private static void EditTitle(IRenderedComponent<FlareConfirmDialogProvider> cut) => Field(cut, "Название").Change("Релиз 2.0");

    [Fact]
    public void Leaving_with_unsaved_changes_asks_first_and_stay_keeps_the_draft()
    {
        var cut = Page();
        EditTitle(cut);

        Navigation.NavigateTo("/tasks");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Остаться").Click();

        Assert.EndsWith($"/tasks/{_task.Id}", Navigation.Uri);
        Assert.Equal("Релиз 2.0", Field(cut, "Название").GetAttribute("value"));
    }

    [Fact]
    public void Confirming_the_loss_leaves_the_page()
    {
        var cut = Page();
        EditTitle(cut);

        Navigation.NavigateTo("/tasks");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Уйти без сохранения").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/tasks", Navigation.Uri));
    }

    [Fact]
    public void Without_changes_the_page_leaves_without_asking()
    {
        var cut = Page();

        Navigation.NavigateTo("/tasks");

        Assert.EndsWith("/tasks", Navigation.Uri);
        Assert.Empty(cut.FindAll("[role='alertdialog']"));
    }

    [Fact]
    public void Reloading_the_same_task_keeps_an_unsaved_draft()
    {
        var cut = Page();
        EditTitle(cut);

        // Задачу перечитали после добавления подзадачи: версия выросла, название на сервере прежнее.
        _task = new TaskModel { Id = _task.Id, Title = "Релиз", Status = "Planned", Version = 2 };
        cut.FindComponent<TaskDetails>().Render(p => p.Add(x => x.Item, _task));

        Assert.Equal("Релиз 2.0", Field(cut, "Название").GetAttribute("value"));
    }
}
