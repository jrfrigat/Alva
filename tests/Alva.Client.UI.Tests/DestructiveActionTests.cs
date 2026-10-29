using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Архивирование спрашивает и называет объект; отмена ничего не отправляет.</summary>
public sealed class DestructiveActionTests : UiTestBase
{
    [Fact]
    public void Archiving_a_task_asks_first_and_cancel_sends_nothing()
    {
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };
        Api.Answer($"tasks/{task.Id}/history", _ => Array.Empty<HistoryDto>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d.Add(x => x.Item, task)));

        Button(cut, "Архивировать задачу").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Assert.Contains("Релиз", cut.Markup);
        Button(cut, "Отмена").Click();
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Delete);
    }

    [Fact]
    public void Archiving_a_project_asks_first_and_cancel_sends_nothing()
    {
        var project = new ProjectModel { Id = Guid.NewGuid(), Name = "Портал", TeamIds = [Api.TeamGuid] };
        Api.Answer("projects", _ => new[] { project })
           .Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
        var cut = Provider<Projects>();

        Button(cut, "Настройки проекта").Click();
        cut.WaitForAssertion(() => Assert.Contains("Настройки проекта", cut.Markup));
        Button(cut, "Архивировать").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Assert.Contains("Портал", cut.Markup);
        Button(cut, "Отмена").Click();
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Delete);
    }

    [Fact]
    public void Archiving_an_actor_asks_first_and_cancel_sends_nothing()
    {
        var actor = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        Api.Answer("actors", _ => new[] { actor })
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Provider<Alva.Client.Pages.Directory>();

        cut.WaitForAssertion(() => Assert.Contains("Анна", cut.Markup));
        Button(cut, "Анна").Click();
        Button(cut, "Архивировать").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Отмена").Click();
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Delete);
    }

    private IRenderedComponent<FlareConfirmDialogProvider> Provider<T>() where T : Microsoft.AspNetCore.Components.IComponent =>
        Render<FlareConfirmDialogProvider>(p => p.AddChildContent<T>());
}
