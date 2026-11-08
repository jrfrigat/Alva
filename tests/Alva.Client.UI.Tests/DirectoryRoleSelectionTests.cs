using Alva.Client.Models;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;
using DirectoryPage = Alva.Client.Pages.Directory;

namespace Alva.Client.UI.Tests;

/// <summary>Навыки в карточке человека выбираются одним множественным списком из справочника.</summary>
public sealed class DirectoryRoleSelectionTests : UiTestBase
{
    [Fact]
    public void Picking_and_removing_a_skill_updates_the_selection_and_the_saved_request()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "Backend", Kind = "Skill" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Иван Петров", Kind = "Person" };
        Api.Answer("actors", _ => new[] { person, skill })
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<DirectoryPage>());

        cut.WaitForAssertion(() => Assert.Contains("Иван Петров", cut.Markup));
        cut.Find(".flare-list-item__action").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".flare-multiselect")));

        OpenPicker(cut);
        Pick(cut, "Backend");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".flare-multiselect__chip")));

        cut.Find(".flare-multiselect__chip-remove").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".flare-multiselect__chip")));

        OpenPicker(cut);
        Pick(cut, "Backend");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".flare-multiselect__chip")));

        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.IsType<ActorRequest>(Api.Sent.Single(x => x.Method == HttpMethod.Put).Value);
            Assert.Equal(skill.Id, Assert.Single(request.SkillIds!));
        });
    }

    private static void OpenPicker<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Find(".flare-multiselect__control").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-multiselect__dropdown .flare-multiselect__option")));
    }

    private static void Pick<T>(IRenderedComponent<T> cut, string label) where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.FindAll(".flare-multiselect__option").First(o => o.TextContent.Contains(label, StringComparison.Ordinal)).Click();
    }
}
