using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>До ответа — скелетон, а не нули и «пусто»; пустая команда получает первый запуск.</summary>
public sealed class LoadingStateTests : UiTestBase
{
    public LoadingStateTests()
    {
        Api.Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
    }

    [Fact]
    public void Overview_shows_a_skeleton_instead_of_zero_metrics_while_loading()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Api.GetGate = gate.Task;

        var cut = Render<Overview>();

        Assert.Contains("alva-loading", cut.Markup);
        Assert.DoesNotContain("Задач в работе", cut.Markup);
        gate.SetResult();
        cut.WaitForAssertion(() => Assert.Contains("Первый запуск", cut.Markup));
    }

    [Fact]
    public void Tasks_shows_a_skeleton_instead_of_the_empty_state_while_loading()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Api.GetGate = gate.Task;

        var cut = Render<Tasks>();

        Assert.Contains("alva-loading", cut.Markup);
        Assert.DoesNotContain("Задач пока нет", cut.Markup);
        gate.SetResult();
        cut.WaitForAssertion(() => Assert.Contains("Задач пока нет", cut.Markup));
    }

    [Fact]
    public void An_empty_team_gets_the_first_run_checklist_and_no_zero_metrics()
    {
        var cut = Render<Overview>();

        cut.WaitForAssertion(() => Assert.Contains("Первый запуск", cut.Markup));
        Assert.Contains("Люди и графики", cut.Markup);
        Assert.Contains("Следующий шаг", cut.Markup);
        Assert.DoesNotContain("Задач в работе", cut.Markup);
        var links = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains("team", links);
        Assert.Contains("projects", links);
        Assert.Contains("tasks", links);
        Assert.Contains("sprints", links);
    }

    [Fact]
    public void Analytics_separates_no_tasks_from_no_risks()
    {
        var cut = Render<Analytics>();

        cut.WaitForAssertion(() => Assert.Contains("Нет задач для оценки", cut.Markup));
        Assert.DoesNotContain("Открытых рисков нет", cut.Markup);
        Assert.Contains("К задачам", cut.Markup);
    }
}
