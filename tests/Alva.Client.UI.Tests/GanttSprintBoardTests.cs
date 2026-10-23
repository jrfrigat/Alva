using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
using Bunit;
using Flare.Components.Gantt;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Пока данных доски нет, единственный скелетон региона объявляет загрузку скринридеру.</summary>
public sealed class GanttSprintBoardTests : UiTestBase
{
    public GanttSprintBoardTests() => Services.AddScoped<GanttPreferences>();

    [Fact]
    public void Loading_skeleton_is_the_announced_status_of_the_region()
    {
        var cut = Render<GanttSprintBoard>(p => p
            .Add(x => x.Sprint, new SprintModel())
            .Add(x => x.Range, GanttSpan.FromDays(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 25))));

        var skeleton = cut.Find(".flare-skeleton");
        Assert.Equal("status", skeleton.GetAttribute("role"));
        Assert.Equal("true", skeleton.GetAttribute("aria-busy"));
        Assert.False(string.IsNullOrWhiteSpace(skeleton.GetAttribute("aria-label")));
        Assert.Null(skeleton.GetAttribute("aria-hidden"));
    }
}
