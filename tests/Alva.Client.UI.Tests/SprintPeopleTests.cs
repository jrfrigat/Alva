using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>A person can be assigned to a task without dragging - with a button in the Sprint people block.</summary>
public sealed class SprintPeopleTests : UiTestBase
{
    private static readonly ActorModel Person = new() { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
    private static readonly SprintModel Sprint = new() { Id = Guid.NewGuid(), Name = "Спринт 1" };

    private IRenderedComponent<SprintPeople> People() => Render<SprintPeople>(p => p
        .Add(x => x.Sprint, Sprint)
        .Add(x => x.Actors, [Person])
        .Add(x => x.Capacity, [new CapacityDto(Person.Id, Person.Name, Sprint.StartDate, 8, 4)]));

    [Fact]
    public void A_person_row_is_a_drag_source_without_a_list_overlay()
    {
        var cut = People();

        Assert.NotEmpty(cut.FindAll(".flare-draggable"));
        // The stretched list action used to cover the draggable and swallowed the pointer press.
        Assert.Empty(cut.FindAll(".flare-list-item__action"));
        Assert.NotNull(cut.Find("button[aria-label='Показать на Гантте: Анна']"));
    }

    [Fact]
    public void The_focus_button_highlights_the_person_on_the_gantt()
    {
        Guid? focused = null;
        var cut = Render<SprintPeople>(p => p
            .Add(x => x.Sprint, Sprint)
            .Add(x => x.Actors, [Person])
            .Add(x => x.Capacity, [new CapacityDto(Person.Id, Person.Name, Sprint.StartDate, 8, 4)])
            .Add(x => x.FocusChanged, (Guid? id) => focused = id));

        cut.Find("button[aria-label='Показать на Гантте: Анна']").Click();

        Assert.Equal(Person.Id, focused);
    }

    [Fact]
    public void A_person_can_be_assigned_to_a_task_without_dragging()
    {
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Задача 1", Status = "Planned" };
        var sprint = new SprintModel { Id = Guid.NewGuid(), Name = "Спринт 1" };
        (Guid PersonId, Guid TaskId) assigned = default;

        var cut = Render<SprintPeople>(p => p
            .Add(x => x.Sprint, sprint)
            .Add(x => x.Actors, [person])
            .Add(x => x.Tasks, [task])
            .Add(x => x.Capacity, [new CapacityDto(person.Id, person.Name, sprint.StartDate, 8, 4)])
            .Add(x => x.OnAssign, ((Guid PersonId, Guid TaskId) value) => assigned = value));

        cut.Find("button[aria-label='Назначить исполнителя: Анна']").Click();

        var select = cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача");
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(o => o.TextContent.Trim() == "Задача 1").Click();
        Button(cut, "Применить назначение").Click();

        Assert.Equal((person.Id, task.Id), assigned);
    }
}
