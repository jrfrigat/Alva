using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Which tasks occupy people: the top-most assignment carries its subtree once.</summary>
public sealed class CapacityPlanTests
{
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Boris = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 11, 9);

    private static Guid Id() => Guid.NewGuid();

    private static PlanTask Task(Guid id, Guid? parent, decimal hours, DateOnly? start = null, DateOnly? end = null,
        Guid? person = null, Guid? target = null) =>
        new(id, parent, hours, start, end, null, target, person, null);

    [Fact]
    public void An_assigned_task_is_one_root_and_its_works_are_not_counted_again()
    {
        var parent = Id();
        var task = Task(parent, null, 24m, Monday, Monday.AddDays(4), person: Anna);
        var child = Task(Id(), parent, 8m, person: null);

        var root = Assert.Single(CapacityPlan.Roots([task, child]));

        Assert.Equal(parent, root.Task.Id);
        Assert.Equal(24m, root.Task.Hours);
        Assert.Equal((Monday, Monday.AddDays(4)), root.Window);
    }

    [Fact]
    public void The_window_of_an_unplanned_task_comes_from_its_works()
    {
        var parent = Id();
        var task = Task(parent, null, 24m, person: Anna);
        var first = Task(Id(), parent, 8m, Monday, Monday.AddDays(1), person: null);
        var second = Task(Id(), parent, 16m, Monday.AddDays(3), Monday.AddDays(4), person: null);

        var root = Assert.Single(CapacityPlan.Roots([task, first, second]));

        Assert.Equal((Monday, Monday.AddDays(4)), root.Window);
    }

    [Fact]
    public void An_unplanned_child_takes_the_dates_of_its_ancestor()
    {
        var parent = Id();
        var task = Task(parent, null, 0m, Monday, Monday.AddDays(4), person: null);
        var child = Task(Id(), parent, 8m, person: Boris);

        var root = Assert.Single(CapacityPlan.Roots([task, child]));

        Assert.Equal(child.Id, root.Task.Id);
        Assert.Equal((Monday, Monday.AddDays(4)), root.Window);
    }

    [Fact]
    public void A_task_under_an_assigned_ancestor_is_not_counted()
    {
        var parent = Id();
        var task = Task(parent, null, 24m, Monday, Monday.AddDays(4), person: Anna);
        var child = Task(Id(), parent, 8m, person: Boris);

        var root = Assert.Single(CapacityPlan.Roots([task, child]));

        Assert.Equal(parent, root.Task.Id);
        Assert.NotEqual(Boris, root.Task.PersonId);
    }

    [Fact]
    public void Unassigned_parents_do_not_hide_their_assigned_works()
    {
        var parent = Id();
        var task = Task(parent, null, 24m, Monday, Monday.AddDays(4), person: null);
        var first = Task(Id(), parent, 8m, Monday, Monday, person: Anna);
        var second = Task(Id(), parent, 16m, Monday, Monday.AddDays(1), person: Boris);

        var roots = CapacityPlan.Roots([task, first, second]).ToArray();

        Assert.Equal(2, roots.Length);
        Assert.Equal([Anna, Boris], roots.Select(x => x.Task.PersonId).ToArray());
    }

    [Fact]
    public void A_task_without_dates_anywhere_is_not_planned()
    {
        var task = Task(Id(), null, 8m, person: Anna);

        Assert.Empty(CapacityPlan.Roots([task]));
    }
}
