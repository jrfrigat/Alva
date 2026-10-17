using Bunit;
using Flare.Components.Gantt;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Flare.Components.Gantt.Tests;

/// <summary>Ширину колонки шапок меняют клавиатурой и кнопками, а не только перетаскиванием.</summary>
public sealed class GanttSideWidthTests : GanttTestBase
{
    private IRenderedComponent<FlareGantt> Resizable(List<string> seen) =>
        Chart(p => p.Add(x => x.Rows, new[] { new Node("r") }),
            g => g.Add(x => x.ResizableSide, true)
                  .Add(x => x.SideWidth, "22rem")
                  .Add(x => x.DefaultSideWidth, "22rem")
                  .Add(x => x.SideWidthChanged, (string width) => seen.Add(width)));

    // На узком экране запрошенные 22rem (352px) шире самого графика (около 284px), и ось начиналась за
    // пределами экрана. Колонка уступает оси: не больше семи десятых графика — тот же предел, что у
    // перетаскивания в gantt.js, поэтому значение из параметра и после перетаскивания ведут себя одинаково.
    [Fact]
    public void The_column_yields_to_the_axis_when_the_chart_is_narrow()
    {
        var cut = Chart(p => p.Add(x => x.Rows, new[] { new Node("r") }),
            g => g.Add(x => x.SideWidth, "22rem"));

        // Пока браузер не сообщил размеров, ширину берут как есть: на широком экране ничего не меняется.
        Assert.Contains("--_gantt-side:22rem", cut.Find(".flare-gantt").GetAttribute("style"));

        cut.InvokeAsync(() => cut.Instance.Viewport(284, 0));

        // 284 × 0.7 = 198,8: колонка шире не станет, и оси остаётся около 85px.
        cut.WaitForAssertion(() => Assert.Contains("--_gantt-side:min(22rem, 199px)", cut.Find(".flare-gantt").GetAttribute("style")));
    }

    [Fact]
    public void The_separator_is_focusable_and_reports_the_width()
    {
        var cut = Resizable([]);

        var handle = cut.Find(".flare-gantt__side-splitter");
        Assert.Equal("0", handle.GetAttribute("tabindex"));
        Assert.Equal("vertical", handle.GetAttribute("aria-orientation"));
        // 22rem = 352px; пол и потолок задают доступные значения разделителя.
        Assert.Equal("352", handle.GetAttribute("aria-valuenow"));
        Assert.Equal("120", handle.GetAttribute("aria-valuemin"));
        Assert.Equal("1200", handle.GetAttribute("aria-valuemax"));
    }

    [Fact]
    public async Task The_splitter_keeps_the_reset_width_and_reports_a_settled_width()
    {
        var seen = new List<string>();
        var cut = Resizable(seen);

        Assert.Equal("352", cut.Find(".flare-gantt__side-splitter").GetAttribute("data-flare-splitter-default"));
        await cut.InvokeAsync(() => cut.FindComponent<FlareSplitter>().Instance.OnSplitChanged("368px"));
        Assert.Equal("368px", Assert.Single(seen));
        Assert.Contains("--_gantt-side:368px", cut.Find(".flare-gantt").GetAttribute("style"));

        await cut.InvokeAsync(() => cut.FindComponent<FlareSplitter>().Instance.OnSplitChanged("352px"));
        Assert.Equal("352px", seen[^1]);

        Assert.Equal("352", cut.Find(".flare-gantt__side-splitter").GetAttribute("data-flare-splitter-default"));
    }

    [Fact]
    public async Task The_separator_limits_the_column_on_a_narrow_chart()
    {
        var cut = Resizable([]);
        await cut.InvokeAsync(() => cut.Instance.Viewport(284, 0));
        cut.WaitForAssertion(() => Assert.Equal("199", cut.Find(".flare-gantt__side-splitter").GetAttribute("aria-valuemax")));
        Assert.Equal("199", cut.Find(".flare-gantt__side-splitter").GetAttribute("aria-valuenow"));
    }
}
