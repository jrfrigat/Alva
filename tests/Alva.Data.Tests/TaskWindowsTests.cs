using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Правило «родитель охватывает детей».</summary>
public sealed class TaskWindowsTests
{
    private static readonly Guid Root = Guid.NewGuid(), Parent = Guid.NewGuid(), Child = Guid.NewGuid(), Sibling = Guid.NewGuid();
    private static DateOnly Oct(int day) => new(2026, 10, day);

    [Fact]
    public void A_child_past_its_parent_widens_every_dated_ancestor()
    {
        TaskDates[] tasks =
        [
            new(Root, null, Oct(1), Oct(20)),
            new(Parent, Root, Oct(5), Oct(14)),
            new(Child, Parent, Oct(10), Oct(22))
        ];

        var updates = TaskWindows.Enforce(tasks, Child);

        Assert.Equal([new TaskDates(Parent, Root, Oct(5), Oct(22)), new TaskDates(Root, null, Oct(1), Oct(22))], updates);
    }

    [Fact]
    public void A_parent_cannot_shrink_below_its_children()
    {
        TaskDates[] tasks =
        [
            new(Parent, null, Oct(8), Oct(9)),
            new(Child, Parent, Oct(5), Oct(7)),
            new(Sibling, Parent, Oct(9), Oct(16))
        ];

        var update = Assert.Single(TaskWindows.Enforce(tasks, Parent));

        Assert.Equal(new TaskDates(Parent, null, Oct(5), Oct(16)), update);
    }

    [Fact]
    public void Nothing_changes_when_the_parent_already_covers()
    {
        TaskDates[] tasks = [new(Parent, null, Oct(1), Oct(31)), new(Child, Parent, Oct(5), Oct(7))];

        Assert.Empty(TaskWindows.Enforce(tasks, Child));
        Assert.Empty(TaskWindows.Enforce(tasks, Parent));
    }

    [Fact]
    public void Undated_tasks_are_left_alone_but_do_not_stop_the_climb()
    {
        TaskDates[] tasks =
        [
            new(Root, null, Oct(1), Oct(10)),
            new(Parent, Root, null, null),
            new(Child, Parent, Oct(8), Oct(12))
        ];

        var update = Assert.Single(TaskWindows.Enforce(tasks, Child));

        Assert.Equal(new TaskDates(Root, null, Oct(1), Oct(12)), update);
    }

    [Fact]
    public void A_fixed_ancestor_that_would_be_widened_is_a_conflict()
    {
        TaskDates[] tasks =
        [
            new(Parent, null, Oct(1), Oct(20), Fixed: true),
            new(Child, Parent, Oct(5), Oct(25))
        ];

        var updates = TaskWindows.Enforce(tasks, Child);
        var conflict = TaskWindows.Conflict(tasks, updates);

        Assert.Equal(Parent, conflict?.Id);
    }

    [Fact]
    public void A_fixed_task_itself_cannot_be_widened_by_its_child()
    {
        TaskDates[] tasks =
        [
            new(Parent, null, Oct(5), Oct(9), Fixed: true),
            new(Child, Parent, Oct(3), Oct(12))
        ];

        var updates = TaskWindows.Enforce(tasks, Parent);

        Assert.Equal(Parent, TaskWindows.Conflict(tasks, updates)?.Id);
    }

    [Fact]
    public void No_conflict_when_the_fixed_window_already_covers_the_change()
    {
        TaskDates[] tasks =
        [
            new(Parent, null, Oct(1), Oct(20), Fixed: true),
            new(Child, Parent, Oct(5), Oct(7))
        ];

        Assert.Null(TaskWindows.Conflict(tasks, TaskWindows.Enforce(tasks, Child)));
    }

    [Fact]
    public void Descendants_cover_grandchildren_and_survive_a_cycle()
    {
        TaskDates[] tasks =
        [
            new(Parent, Child, Oct(5), Oct(6)),
            new(Child, Parent, Oct(3), Oct(4)),
            new(Sibling, Child, Oct(10), Oct(11))
        ];

        Assert.Equal((Oct(3), Oct(11)), TaskWindows.Descendants(tasks, Parent));
    }
}
