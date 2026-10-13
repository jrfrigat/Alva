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
        Assert.Contains("--flare-gantt-side:22rem", cut.Find(".flare-gantt").GetAttribute("style"));

        cut.InvokeAsync(() => cut.Instance.Viewport(284, 0));

        // 284 × 0.7 = 198,8: колонка шире не станет, и оси остаётся около 85px.
        cut.WaitForAssertion(() => Assert.Contains("--flare-gantt-side:min(22rem, 199px)", cut.Find(".flare-gantt").GetAttribute("style")));
    }

    [Fact]
    public void The_separator_is_focusable_and_reports_the_width()
    {
        var cut = Resizable([]);

        var handle = cut.Find(".flare-gantt__side-handle");
        Assert.Equal("0", handle.GetAttribute("tabindex"));
        Assert.Equal("vertical", handle.GetAttribute("aria-orientation"));
        // 22rem = 352px; пол и потолок задают доступные значения разделителя.
        Assert.Equal("352", handle.GetAttribute("aria-valuenow"));
        Assert.Equal("120", handle.GetAttribute("aria-valuemin"));
        Assert.Equal("1200", handle.GetAttribute("aria-valuemax"));
    }

    [Fact]
    public void Arrow_keys_change_the_width_and_Home_resets_it()
    {
        var seen = new List<string>();
        var cut = Resizable(seen);

        cut.Find(".flare-gantt__side-handle").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal("368px", Assert.Single(seen));
        Assert.Contains("--flare-gantt-side:368px", cut.Find(".flare-gantt").GetAttribute("style"));

        cut.Find(".flare-gantt__side-handle").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Equal("352px", seen[^1]);

        cut.Find(".flare-gantt__side-handle").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal("22rem", seen[^1]);
    }

    [Fact]
    public void The_step_buttons_change_and_reset_the_width_without_dragging()
    {
        var seen = new List<string>();
        var cut = Resizable(seen);

        cut.FindAll(".flare-gantt__side-button")[1].Click(); // шире
        Assert.Equal("368px", seen[^1]);

        cut.FindAll(".flare-gantt__side-button")[0].Click(); // уже
        Assert.Equal("352px", seen[^1]);

        cut.FindAll(".flare-gantt__side-button")[2].Click(); // сброс
        Assert.Equal("22rem", seen[^1]);
    }
}
