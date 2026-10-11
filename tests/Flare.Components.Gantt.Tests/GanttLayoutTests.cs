using Bunit;
using Flare.Components.Gantt;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Flare.Components.Gantt.Tests;

/// <summary>Ширина оси, колонка шапок и упор покрытия при растяжении.</summary>
public sealed class GanttLayoutTests : GanttTestBase
{
    private static readonly Node[] OneRow = [new Node("r1")];
    private static readonly Work[] Parent = [new Work("p", "r1", Day(2), Day(5))];

    [Fact]
    public void Fill_width_stretches_units_to_the_visible_width()
    {
        var cut = Chart(p => p.Add(x => x.Rows, OneRow).Add(x => x.Items, Parent), g => g.Add(x => x.FillWidth, true));

        // 14 дней по 40 px = 560 px; видно 1000 px, из них 160 px колонка шапок.
        cut.InvokeAsync(() => cut.Instance.Viewport(1000, 160));

        Assert.Equal(59.92, cut.Instance.EffectiveUnitWidth, 2);
        Assert.StartsWith("left:119.84px;", cut.Find(".flare-gantt__bar-slot").GetAttribute("style"));
    }

    [Fact]
    public void Unit_width_is_the_floor_and_fill_is_opt_in()
    {
        var narrow = Chart(p => p.Add(x => x.Rows, OneRow), g => g.Add(x => x.FillWidth, true));
        narrow.InvokeAsync(() => narrow.Instance.Viewport(300, 160));
        var plain = Chart(p => p.Add(x => x.Rows, OneRow));
        plain.InvokeAsync(() => plain.Instance.Viewport(2000, 160));

        Assert.Equal(40, narrow.Instance.EffectiveUnitWidth);
        Assert.Equal(40, plain.Instance.EffectiveUnitWidth);
    }

    [Fact]
    public void A_new_width_that_keeps_the_unit_does_not_rerender()
    {
        var cut = Chart(p => p.Add(x => x.Rows, OneRow).Add(x => x.Items, Parent), g => g.Add(x => x.FillWidth, true));
        var renders = cut.RenderCount;

        // Ось шире видимого — день остаётся 40 px, сколько бы ни менялась ширина окна (анимация меню).
        foreach (var width in new[] { 500d, 480, 460, 440 }) cut.InvokeAsync(() => cut.Instance.Viewport(width, 160));

        Assert.Equal(40, cut.Instance.EffectiveUnitWidth);
        Assert.Equal(renders, cut.RenderCount);
    }

    [Fact]
    public void Virtualized_panels_draw_only_rows_near_the_visible_part()
    {
        var rows = Enumerable.Range(0, 200).Select(i => new Node($"r{i}")).ToArray();
        var cut = Chart(p => p.Add(x => x.Rows, rows), g => g.Add(x => x.Virtualize, true));
        int Drawn() => cut.FindAll(".flare-gantt__row").Count;

        // Строки по 30 px: до того как браузер сообщит видимое, рисуется первый экран с запасом — не все 200.
        Assert.InRange(Drawn(), 30, 90);
        Assert.Empty(cut.FindAll(".flare-gantt__spacer"));
        Assert.Equal("6000px", cut.Find(".flare-gantt__body").GetAttribute("style")!.Split(':')[1]);

        var panel = cut.Find(".flare-gantt__body").GetAttribute("data-panel")!;
        cut.InvokeAsync(() => cut.Instance.Visible([panel], [3000], [3600]));

        Assert.InRange(Drawn(), 20, 80);
        Assert.Equal("height:2400px", cut.Find(".flare-gantt__spacer").GetAttribute("style"));
        Assert.Equal("r80", cut.Find(".flare-gantt__row").GetAttribute("data-row"));
    }

    [Fact]
    public void Without_virtualize_every_row_is_drawn()
    {
        var rows = Enumerable.Range(0, 200).Select(i => new Node($"r{i}")).ToArray();
        var cut = Chart(p => p.Add(x => x.Rows, rows));

        Assert.Equal(200, cut.FindAll(".flare-gantt__row").Count);
    }

    [Fact]
    public void Dragged_side_width_is_kept_and_reported()
    {
        string? reported = null;
        var cut = Chart(p => p.Add(x => x.Rows, OneRow), g => g
            .Add(x => x.ResizableSide, true)
            .Add(x => x.SideWidth, "12rem")
            .Add(x => x.SideWidthChanged, (string w) => reported = w));

        Assert.NotNull(cut.Find(".flare-gantt__side-handle[role=separator]"));
        cut.InvokeAsync(() => cut.Instance.SideResized(287.6));

        Assert.Equal("288px", reported);
        Assert.Contains("--flare-gantt-side:288px", cut.Find(".flare-gantt").GetAttribute("style"));
    }

    [Fact]
    public void No_handle_unless_asked()
    {
        var cut = Chart(p => p.Add(x => x.Rows, OneRow));

        Assert.Empty(cut.FindAll(".flare-gantt__side-handle"));
    }

    [Fact]
    public void Resizing_stops_at_the_span_the_item_must_cover()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cover = GanttSpan.FromDays(Day(1), Day(4)); // дети начинаются раньше родителя и кончаются на день раньше
        var cut = Chart(p => p.Add(x => x.Rows, OneRow).Add(x => x.Items, Parent).Add(x => x.Editable, true)
            .Add(x => x.ItemCover, (Func<Work, GanttSpan?>)(_ => cover))
            .Add(x => x.OnChange, (GanttChangeEventArgs e) => seen.Add(e)));

        var bar = cut.Find(".flare-gantt__bar[data-key='p']");
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", CtrlKey = true, ShiftKey = true }); // конец 5 → 4: можно
        cut.Find(".flare-gantt__bar[data-key='p'] .flare-gantt__handle--start").PointerDown(new PointerEventArgs { ClientX = 0, Button = 0 });
        cut.Find(".flare-gantt__body").PointerMove(new PointerEventArgs { ClientX = 120 });
        cut.Find(".flare-gantt__body").PointerUp(new PointerEventArgs { ClientX = 120 });

        Assert.Equal(GanttSpan.FromDays(Day(2), Day(4)), seen[0].Changes[0].Span);
        // Начало 2 и так позже начала детей (1) — тянуть вправо нельзя, полоса встаёт на начало покрытия.
        Assert.Equal(GanttSpan.FromDays(Day(1), Day(5)), seen[1].Changes[0].Span);
    }
}
