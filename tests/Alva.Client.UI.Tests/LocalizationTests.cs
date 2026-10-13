using System.Globalization;
using Alva.Client;
using Alva.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The interface ships in English and Russian; the device remembers the choice.</summary>
public sealed class LocalizationTests : UiTestBase
{
    [Fact]
    public void A_known_string_is_translated_for_the_current_language()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AppStrings>>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Sign out", localizer["Выйти"].Value);
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Выйти", localizer["Выйти"].Value);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void The_static_accessor_reads_the_same_resources()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Sign out", Strings.T("Выйти"));
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Выйти", Strings.T("Выйти"));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void The_interface_renders_in_english_when_the_language_is_english()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var cut = Render<Alva.Client.Pages.Login>();
            Assert.Contains("Sign in", cut.Markup);

            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            var russian = Render<Alva.Client.Pages.Login>();
            Assert.Contains("Войти", russian.Markup);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void An_unknown_key_falls_back_to_itself()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AppStrings>>();

        Assert.Equal("нет такого ключа", localizer["нет такого ключа"].Value);
    }

    [Fact]
    public void Without_a_remembered_choice_the_language_is_english()
    {
        var cultures = Services.GetRequiredService<CultureService>();

        Assert.Equal("en", cultures.Current);
        Assert.Equal(["en", "ru"], CultureService.Supported);
    }

    [Fact]
    public async Task Switching_the_language_changes_the_culture()
    {
        var cultures = Services.GetRequiredService<CultureService>();

        await cultures.SetAsync("ru");

        Assert.Equal("ru", cultures.Current);
        Assert.Equal("ru", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }
}
