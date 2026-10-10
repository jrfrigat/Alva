using AngleSharp.Dom;
using Bunit;
using Flare.Components.Gantt;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Flare.Components.Gantt.Tests;

public sealed class GanttInteractionTests : GanttTestBase
{
    private static readonly Node[] TwoRows = [new Node("r1"), new Node("r2")];
    private static readonly Work[] Plan = [new Work("a", "r1", Day(0), Day(1)), new Work("b", "r1", Day(2), Day(3))];

    private IRenderedComponent<FlareGantt> Editable(List<GanttChangeEventArgs> seen, bool cancel = false, GanttLink[]? links = null,
        Action<ComponentParameterCollectionBuilder<FlareGanttPanel<Node, Work>>>? more = null) =>
        Chart(p =>
        {
            p.Add(x => x.Rows, TwoRows).Add(x => x.Items, Plan).Add(x => x.Editable, true)
             .Add(x => x.Links, links ?? [new GanttLink("a", "b")])
             .Add(x => x.OnChange, (GanttChangeEventArgs e) => { seen.Add(e); e.Cancel = cancel; });
            more?.Invoke(p);
        });

    private static IElement Bar(IRenderedComponent<FlareGantt> cut, string key) => cut.Find($".flare-gantt__bar[data-key='{key}']");

    [Fact]
    public void Ctrl_arrow_moves_one_unit_and_pushes_successors()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen);

        Bar(cut, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true });

        var change = Assert.Single(seen);
        Assert.Equal(GanttEditAction.Move, change.Action);
        Assert.Equal([new GanttChange("a", GanttSpan.FromDays(Day(1), Day(2))), new GanttChange("b", GanttSpan.FromDays(Day(3), Day(4)))], change.Changes);
        Assert.Contains("a перенесён: 13.10.2026 – 14.10.2026.", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void Ctrl_shift_arrow_resizes_the_end_and_never_inverts()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen, links: []);

        Bar(cut, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true, ShiftKey = true });
        Bar(cut, "b").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", CtrlKey = true, ShiftKey = true });
        cut.Find(".flare-gantt__bar[data-key='a'] .flare-gantt__handle--start").PointerDown(new PointerEventArgs { ClientX = 0, Button = 0 });
        cut.Find(".flare-gantt__body").PointerMove(new PointerEventArgs { ClientX = 200 });
        cut.Find(".flare-gantt__body").PointerUp(new PointerEventArgs { ClientX = 200 });

        Assert.Equal(GanttEditAction.ResizeEnd, seen[0].Action);
        Assert.Equal(GanttSpan.FromDays(Day(0), Day(2)), seen[0].Changes[0].Span);
        Assert.Equal(GanttSpan.FromDays(Day(2), Day(2)), seen[1].Changes[0].Span);
        Assert.Equal(GanttEditAction.ResizeStart, seen[2].Action);
        Assert.Equal(new GanttSpan(Day(2).ToDateTime(TimeOnly.MinValue), Day(2).ToDateTime(TimeOnly.MinValue)), seen[2].Changes[0].Span); // start stops at the end
    }

    [Fact]
    public void Ctrl_down_moves_to_the_next_row()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen);

        Bar(cut, "b").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", CtrlKey = true });
        Bar(cut, "b").KeyDown(new KeyboardEventArgs { Key = "ArrowUp", CtrlKey = true }); // already on the first row

        var change = Assert.Single(seen);
        Assert.Equal("r2", change.Changes[0].RowKey);
        Assert.Single(change.Changes);
    }

    [Fact]
    public void A_cancelled_or_unapplied_change_leaves_the_chart_as_it_was()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen, cancel: true);
        string Geometry() => string.Join("|", cut.FindAll(".flare-gantt__bar-slot, path.flare-gantt__link").Select(e => e.GetAttribute("style") ?? e.GetAttribute("d")));
        var before = Geometry();

        Bar(cut, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true });

        Assert.Single(seen);
        Assert.Equal(before, Geometry());
        Assert.Empty(cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void Pointer_drag_previews_and_commits_whole_units()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen, links: []);
        var body = cut.Find(".flare-gantt__body");

        Bar(cut, "a").PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 10, Button = 0 });
        body.PointerMove(new PointerEventArgs { ClientX = 185, ClientY = 12 });

        Assert.Contains("flare-gantt__bar--preview", Bar(cut, "a").ClassList);
        Assert.Contains("left:80px", Bar(cut, "a").ParentElement!.GetAttribute("style"));

        cut.Find(".flare-gantt__body").PointerUp(new PointerEventArgs { ClientX = 185, ClientY = 12 });
        Bar(cut, "a").Click(); // the click that follows a drag does not select

        var change = Assert.Single(seen);
        Assert.Equal(GanttSpan.FromDays(Day(2), Day(3)), change.Changes[0].Span);
        Assert.Empty(cut.FindAll(".flare-gantt__bar--selected"));
        Assert.Empty(cut.FindAll(".flare-gantt__bar--preview"));
    }

    [Fact]
    public void Pointer_drag_down_changes_the_row_and_the_end_handle_resizes()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen, links: []);

        Bar(cut, "a").PointerDown(new PointerEventArgs { ClientX = 10, ClientY = 15, Button = 0 });
        cut.Find(".flare-gantt__body").PointerMove(new PointerEventArgs { ClientX = 10, ClientY = 45 });
        cut.Find(".flare-gantt__body").PointerUp(new PointerEventArgs { ClientX = 10, ClientY = 45 });
        Assert.Equal("r2", seen[0].Changes[0].RowKey);

        cut.Find(".flare-gantt__bar[data-key='b'] .flare-gantt__handle--end").PointerDown(new PointerEventArgs { ClientX = 0, Button = 0 });
        cut.Find(".flare-gantt__body").PointerMove(new PointerEventArgs { ClientX = 80 });
        cut.Find(".flare-gantt__body").PointerUp(new PointerEventArgs { ClientX = 80 });
        Assert.Equal(GanttEditAction.ResizeEnd, seen[1].Action);
        Assert.Equal(GanttSpan.FromDays(Day(2), Day(5)), seen[1].Changes[0].Span);
    }

    [Fact]
    public void Leaving_the_surface_cancels_the_gesture()
    {
        var seen = new List<GanttChangeEventArgs>();
        var cut = Editable(seen);

        Bar(cut, "a").PointerDown(new PointerEventArgs { ClientX = 0, Button = 0 });
        cut.Find(".flare-gantt__body").PointerMove(new PointerEventArgs { ClientX = 120 });
        cut.Find(".flare-gantt__body").PointerLeave(new PointerEventArgs());
        cut.Find(".flare-gantt__body").PointerUp(new PointerEventArgs { ClientX = 120 });

        Assert.Empty(seen);
        Assert.Empty(cut.FindAll(".flare-gantt__bar--preview"));
    }

    [Fact]
    public void Links_are_created_from_the_keyboard_and_cycles_are_refused()
    {
        var created = new List<GanttLink>();
        var rejected = new List<GanttLinkRejectedEventArgs>();
        var items = new[] { new Work("a", "r1", Day(0), Day(1)), new Work("b", "r1", Day(2), Day(3)), new Work("c", "r2", Day(4), Day(5)) };
        var cut = Chart(p => p.Add(x => x.Rows, TwoRows).Add(x => x.Items, items).Add(x => x.Editable, true)
            .Add(x => x.Links, new[] { new GanttLink("a", "b"), new GanttLink("b", "c") })
            .Add(x => x.OnLinkCreate, (GanttLinkEventArgs e) => created.Add(e.Link))
            .Add(x => x.OnLinkRejected, (GanttLinkRejectedEventArgs e) => rejected.Add(e)));

        Bar(cut, "c").KeyDown(new KeyboardEventArgs { Key = "l" });
        Assert.Contains("flare-gantt__bar--link-source", Bar(cut, "c").ClassList);
        Bar(cut, "a").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        var refusal = Assert.Single(rejected);
        Assert.Equal(GanttValidationCode.Cycle, refusal.Validation.Code);
        Assert.Equal("Связь создаёт цикл: c → a → b → c.", refusal.Message);
        Assert.Equal(refusal.Message, cut.Find("[role=status]").TextContent);

        Bar(cut, "a").KeyDown(new KeyboardEventArgs { Key = "L" });
        Bar(cut, "c").Click();
        Assert.Equal([new GanttLink("a", "c")], created);
        Assert.Empty(cut.FindAll(".flare-gantt__bar--link-source"));
    }

    [Fact]
    public void Read_only_and_compact_charts_raise_nothing()
    {
        var seen = new List<GanttChangeEventArgs>();
        var readOnly = Chart(p => p.Add(x => x.Rows, TwoRows).Add(x => x.Items, Plan)
            .Add(x => x.OnChange, (GanttChangeEventArgs e) => seen.Add(e)));
        Bar(readOnly, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true });
        Assert.Empty(readOnly.FindAll(".flare-gantt__handle"));

        var compact = Chart(p => p.Add(x => x.Rows, TwoRows).Add(x => x.Items, Plan).Add(x => x.Editable, true)
            .Add(x => x.OnChange, (GanttChangeEventArgs e) => seen.Add(e)), g => g.Add(x => x.Compact, true));
        Bar(compact, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true });

        Assert.Empty(seen);
    }

    [Fact]
    public void Drop_zones_cover_every_row_and_unit_with_parseable_targets()
    {
        var cut = Chart(p => p.Add(x => x.Rows, TwoRows).Add(x => x.DropGroup, "pool"));

        var zones = cut.FindAll(".flare-gantt__drop");
        Assert.Equal(2 * 14, zones.Count);
        var target = GanttDropTarget.Format("r2", GanttSpan.Day(Day(3)));
        Assert.True(GanttDropTarget.TryParse(target, out var parsed));
        Assert.Equal(new GanttDropTarget("r2", GanttSpan.Day(Day(3))), parsed);
        Assert.False(GanttDropTarget.TryParse("something else", out _));
    }
}
