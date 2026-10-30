using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Alva.Theme.Alva;
using Bunit;
using Flare.Abstractions.Tokens;
using Flare.Components;
using Flare.Extensions;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Текущий контекст виден, а участника можно изменить или убрать с подтверждением.</summary>
public sealed class WorkspaceMemberTests : UiTestBase
{
    private readonly Guid _workspace = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly MemberDto[] _members;

    public WorkspaceMemberTests()
    {
        _members =
        [
            new MemberDto(_owner, "owner@company.ru", "Владелец", true, []),
            new MemberDto(_other, "other@company.ru", "Другой", false, [])
        ];
        Api.User = new UserInfo(_owner, "owner@company.ru", "Владелец");
        Api.SelectedWorkspaceId = _workspace;
        Api.WorkspaceList = [new SpaceInfo(_workspace, "Пространство")];
        Api.Answer("api/workspaces", _ => new[] { new WorkspaceDto(_workspace, "Пространство", false) })
           .Answer($"api/workspaces/{_workspace}/members", _ => _members);
    }

    private IRenderedComponent<FlareThemeProvider> Page()
    {
        Services.AddFlareTheme(AlvaTheme.Create());
        return Render<FlareThemeProvider>(p => p.AddChildContent<FlareConfirmDialogProvider>(c => c.AddChildContent<Settings>()));
    }

    [Fact]
    public void The_current_workspace_has_an_explicit_label()
    {
        var cut = Render<Workspaces>();

        cut.WaitForAssertion(() => Assert.Contains("Пространство и команды", cut.Markup));
        Assert.Contains("Текущее", cut.Markup);
    }

    [Fact]
    public void A_member_can_be_edited_and_self_cannot_be_removed()
    {
        var cut = Page();
        cut.WaitForAssertion(() => Assert.Contains("Другой", cut.Markup));

        var remove = cut.FindAll("button").Where(b => b.TextContent.Trim() == "Убрать").ToArray();
        Assert.True(remove[0].HasAttribute("disabled"));
        Assert.False(remove[1].HasAttribute("disabled"));

        cut.FindAll("button").Where(b => b.TextContent.Trim() == "Изменить").ElementAt(1).Click();

        Assert.Equal("other@company.ru", Field(cut, "Почта участника").GetAttribute("value"));
    }

    [Fact]
    public void Removing_a_member_asks_first_and_sends_delete()
    {
        var cut = Page();
        cut.WaitForAssertion(() => Assert.Contains("Другой", cut.Markup));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Убрать" && !b.HasAttribute("disabled")).Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        cut.FindAll("[role='alertdialog'] button").First(b => b.TextContent.Contains("Убрать")).Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Delete && s.Path.EndsWith($"/members/{_other}", StringComparison.Ordinal)));
    }
}
