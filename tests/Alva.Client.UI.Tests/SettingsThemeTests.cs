using Alva.Client.Pages;
using Bunit;
using Flare.Abstractions;
using Flare.Abstractions.Tokens;
using Flare.Components;
using Flare.Extensions;
using Alva.Theme.Alva;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

public sealed class SettingsThemeTests : UiTestBase
{
    [Fact]
    public void Selecting_a_mode_calls_the_theme_service()
    {
        Services.AddFlareTheme(AlvaTheme.Create());
        var theme = Services.GetRequiredService<IThemeService>();
        var cut = Render<FlareThemeProvider>(p => p.AddChildContent<Settings>());
        var select = cut.FindComponent<FlareSelect<ThemeMode>>();
        select.Find("[role=combobox]").Click();
        cut.FindAll("[role=option]").Single(e => e.TextContent.Trim() == "Тёмный").Click();
        cut.WaitForAssertion(() => Assert.Equal(ThemeMode.Dark, theme.Mode));
    }
}
