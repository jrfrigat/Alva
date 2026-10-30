using Alva.Client.Layout;
using Alva.Client.Pages;
using Alva.Client.Services;
using Bunit;
using Flare.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Ни один редактор не теряет правки молча: закрытие, уход и смена языка спрашивают.</summary>
public sealed class DraftEditorsTests : UiTestBase
{
    [Fact]
    public void A_new_task_draft_survives_stay_and_goes_only_after_confirmation()
    {
        AnswerTasks();
        var cut = Provider<Tasks>();

        Button(cut, "Новая задача").Click();
        Field(cut, "Название").Change("Черновик");
        Button(cut, "Закрыть").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Остаться").Click();
        Assert.Equal("Черновик", Field(cut, "Название").GetAttribute("value"));

        Button(cut, "Закрыть").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Уйти без сохранения").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("textarea")));
    }

    [Fact]
    public void A_new_sprint_draft_asks_before_closing()
    {
        Api.Answer("sprints", _ => Array.Empty<Alva.Client.Models.SprintModel>())
           .Answer("tasks", _ => Array.Empty<Alva.Client.Models.TaskModel>());
        var cut = Provider<Sprints>();

        Button(cut, "Новый спринт").Click();
        Field(cut, "Название").Change("Черновик");
        Button(cut, "Закрыть").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Остаться").Click();
        Assert.Equal("Черновик", Field(cut, "Название").GetAttribute("value"));
    }

    [Fact]
    public void A_new_project_draft_asks_before_closing()
    {
        Api.Answer("projects", _ => Array.Empty<Alva.Client.Models.ProjectModel>())
           .Answer("tasks", _ => Array.Empty<Alva.Client.Models.TaskModel>())
           .Answer("sprints", _ => Array.Empty<Alva.Client.Models.SprintModel>())
           .Answer("actors", _ => Array.Empty<Alva.Client.Models.ActorModel>())
           .Answer("risks", _ => Array.Empty<Alva.Contracts.RiskDto>());
        var cut = Provider<Projects>();

        Button(cut, "Новый проект").Click();
        Field(cut, "Название").Change("Черновик");
        Button(cut, "Закрыть").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Остаться").Click();
        Assert.Equal("Черновик", Field(cut, "Название").GetAttribute("value"));
    }

    [Fact]
    public void A_new_actor_draft_asks_before_closing()
    {
        Api.Answer("actors", _ => Array.Empty<Alva.Client.Models.ActorModel>())
           .Answer("tasks", _ => Array.Empty<Alva.Client.Models.TaskModel>());
        var cut = Provider<Alva.Client.Pages.Directory>();

        Button(cut, "Добавить").Click();
        Field(cut, "Имя или название").Change("Черновик");
        Button(cut, "Закрыть").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Остаться").Click();
        Assert.Equal("Черновик", Field(cut, "Имя или название").GetAttribute("value"));
    }

    [Fact]
    public void Switching_the_language_with_a_draft_asks_in_the_app_dialog_and_cancel_keeps_it()
    {
        Services.GetRequiredService<DraftGuard>().Track(() => true);
        var cultures = Services.GetRequiredService<CultureService>();
        var before = cultures.Current;
        var cut = Render<MainLayout>();

        Button(cut, "Язык").Click();
        cut.WaitForAssertion(() => Assert.Contains("при смене языка", cut.Find("[role='alertdialog']").TextContent));
        Button(cut, "Отмена").Click();

        Assert.Equal(before, cultures.Current);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "confirm");
    }

    [Fact]
    public void Confirming_the_dialog_switches_the_language()
    {
        Services.GetRequiredService<DraftGuard>().Track(() => true);
        var cultures = Services.GetRequiredService<CultureService>();
        var before = cultures.Current;
        var cut = Render<MainLayout>();

        Button(cut, "Язык").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Сменить язык").Click();

        cut.WaitForAssertion(() => Assert.NotEqual(before, cultures.Current));
    }

    private IRenderedComponent<FlareConfirmDialogProvider> Provider<T>() where T : Microsoft.AspNetCore.Components.IComponent =>
        Render<FlareConfirmDialogProvider>(p => p.AddChildContent<T>());

    private void AnswerTasks() =>
        Api.Answer("tasks", _ => Array.Empty<Alva.Client.Models.TaskModel>())
           .Answer("projects", _ => Array.Empty<Alva.Client.Models.ProjectModel>())
           .Answer("sprints", _ => Array.Empty<Alva.Client.Models.SprintModel>())
           .Answer("actors", _ => Array.Empty<Alva.Client.Models.ActorModel>());
}
