using Alva.Client.Models;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;
using DirectoryPage = Alva.Client.Pages.Directory;

namespace Alva.Client.UI.Tests;

/// <summary>A person's sub-team is set by periods, not by a single drop-down list.</summary>
public sealed class DirectoryMembershipTests : UiTestBase
{
    [Fact]
    public void Adding_a_period_saves_its_sub_team_and_dates()
    {
        var department = new ActorModel { Id = Guid.NewGuid(), Name = "Разработка", Kind = "Department" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Иван Петров", Kind = "Person" };
        Api.Answer("actors", _ => new[] { department, person })
           .Answer("tasks", _ => Array.Empty<TaskModel>());
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<DirectoryPage>());

        cut.WaitForAssertion(() => Assert.Contains("Иван Петров", cut.Markup));
        cut.Find(".flare-list-item__action").Click();
        cut.WaitForAssertion(() => Assert.Contains("Добавить период", cut.Markup));
        Button(cut, "Добавить период").Click();
        cut.WaitForAssertion(() => Assert.Contains("Периоды подкоманд", cut.Markup));
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.IsType<ActorRequest>(Api.Sent.Single(x => x.Method == HttpMethod.Put).Value);
            var membership = Assert.Single(request.DepartmentMemberships!);
            Assert.Equal(department.Id, membership.DepartmentId);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today), membership.From);
            Assert.Null(membership.To);
        });
    }
}
