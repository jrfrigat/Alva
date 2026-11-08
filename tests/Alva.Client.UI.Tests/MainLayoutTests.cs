using Alva.Client.Layout;
using Alva.Client.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The shell has exactly one main landmark, and the skip link leads into it.
/// A signed-in user can sign out directly from the header.</summary>
public sealed class MainLayoutTests : UiTestBase
{
    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void The_shell_has_one_main_landmark_that_is_the_skip_link_target()
    {
        var cut = Render<MainLayout>();

        // FlareLayoutContent renders the only <main>; there must be no <main> wrapper of its own.
        var main = cut.FindAll("main");
        Assert.Single(main);
        Assert.Equal("main-content", main[0].Id);
        // The container is focused programmatically, otherwise the skip link cannot move focus into the content.
        Assert.Equal("-1", main[0].GetAttribute("tabindex"));

        // The target of the skip-to-content link is the same main landmark.
        Assert.Equal("#main-content", cut.Find("a.skip-link").GetAttribute("href"));
    }

    [Fact]
    public void The_signed_in_shell_offers_a_way_to_log_out()
    {
        Api.User = new UserInfo(Guid.NewGuid(), "anna@company.ru", "Анна");
        var cut = Render<MainLayout>();

        Button(cut, "Выйти").Click();

        Assert.EndsWith("/login", Navigation.Uri);
    }

    // The phone bottom bar carries short labels that fit, while the full names remain the accessible name and tooltip.
    [Fact]
    public void The_phone_navigation_is_short_while_the_drawer_keeps_the_full_names()
    {
        // Four items is the bottom bar limit; each one must have a short label that fits.
        var primary = NavigationItems.Primary;
        Assert.InRange(primary.Length, 1, 5);
        Assert.All(primary, item => Assert.False(string.IsNullOrWhiteSpace(item.ShortLabel)));
        Assert.All(primary, item => Assert.True(item.ShortLabel!.Length <= 10, $"«{item.ShortLabel}» не поместится на узком экране"));
        Assert.Equal(primary.Length, primary.Select(item => item.ShortLabel).Distinct().Count());

        // The full names are still there: the side menu and the item tooltip show them.
        var cut = Render<MainLayout>();
        foreach (var item in primary) Assert.Contains(item.Label, cut.Markup);
    }
}
