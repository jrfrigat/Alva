using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Services;
using Alva.Contracts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>A change made by another client reaches the task tree without a full reload.</summary>
public sealed class RealtimeViewTests : UiTestBase
{
    private readonly FakeRealtimeService _realtime = new();
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Старая", Status = "Planned", Version = 1 };

    public RealtimeViewTests()
    {
        Services.AddScoped<IRealtimeService>(_ => _realtime);
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
    }

    [Fact]
    public void A_task_changed_elsewhere_refreshes_the_tree_without_a_loading_bar()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Contains("Старая", cut.Markup));
        Assert.True(_realtime.Connected);

        Api.Answer("tasks", _ => new[] { new TaskModel { Id = _task.Id, Title = "Новая", Status = "Planned", Version = 2 } });
        _realtime.Change(new TaskChangedEvent(Api.TeamGuid, _task.Id, 2, "updated"));

        cut.WaitForAssertion(() => Assert.Contains("Новая", cut.Markup));
        Assert.DoesNotContain("flare-progress-linear", cut.Markup);
    }

    [Fact]
    public async Task A_draft_holds_the_realtime_refresh_back()
    {
        var applied = 0;
        var refresh = new RealtimeRefresh(() => false);

        refresh.Schedule(() => { applied++; return Task.CompletedTask; });
        await Task.Delay(400);

        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task A_burst_of_events_collapses_into_one_reload()
    {
        var applied = 0;
        var refresh = new RealtimeRefresh(() => true);

        for (var i = 0; i < 5; i++)
            refresh.Schedule(() => { Interlocked.Increment(ref applied); return Task.CompletedTask; });
        await Task.Delay(450);

        Assert.Equal(1, applied);
    }
}
