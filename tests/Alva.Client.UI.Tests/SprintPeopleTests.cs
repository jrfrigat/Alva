using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Человека можно назначить на задачу без перетаскивания — кнопкой в блоке «Люди спринта».</summary>
public sealed class SprintPeopleTests : UiTestBase
{
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
