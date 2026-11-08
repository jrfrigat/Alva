using Alva.Client.Models;
using Bunit;
using Flare.Components;
using Xunit;
using DirectoryPage = Alva.Client.Pages.Directory;

namespace Alva.Client.UI.Tests;

/// <summary>Selecting a record in the directory highlights the row and does not stretch the list card.</summary>
public sealed class DirectorySelectionTests : UiTestBase
{
    [Fact]
    public void Selecting_a_person_marks_the_row_and_closing_clears_it()
    {
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Иван Петров", Kind = "Person" };
        Api.Answer("actors", _ => new[] { person })
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<DirectoryPage>());

        cut.WaitForAssertion(() => Assert.Contains("Иван Петров", cut.Markup));
        Assert.Empty(cut.FindAll(".flare-list-item--selected"));
        Assert.Single(cut.FindAll(".flare-grid--align-start"));

        cut.Find(".flare-list-item__action").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".flare-list-item--selected")));
        Assert.Contains("Иван Петров", cut.Find(".flare-list-item--selected").TextContent);

        Button(cut, "Закрыть").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".flare-list-item--selected")));
    }

    [Fact]
    public void Selecting_another_row_moves_the_highlight()
    {
        var first = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var second = new ActorModel { Id = Guid.NewGuid(), Name = "Борис", Kind = "Person" };
        Api.Answer("actors", _ => new[] { first, second })
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<DirectoryPage>());

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".flare-list-item__action").Count));
        cut.FindAll(".flare-list-item__action")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains("Анна", cut.Find(".flare-list-item--selected").TextContent));

        cut.FindAll(".flare-list-item__action")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("Борис", cut.Find(".flare-list-item--selected").TextContent));
        Assert.Single(cut.FindAll(".flare-list-item--selected"));
    }
}
