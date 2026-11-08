using System.Globalization;
using Alva.Client;
using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Services;
using Alva.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>
/// Localization is the combination of resources, typed access and the current culture. The tests check
/// the wiring itself, not a specific translation: otherwise every text edit would break the tests.
/// </summary>
public sealed class LocalizationTests : UiTestBase
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void The_localizer_and_the_typed_class_read_the_same_resource(string culture)
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AppStrings>>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var typed = AlvaStrings.Nav_SignOut;

            Assert.False(string.IsNullOrWhiteSpace(typed));
            Assert.Equal(typed, localizer["Nav_SignOut"].Value);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void The_runtime_accessor_resolves_dynamic_keys(string culture)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var text = Strings.T("reason.risk.overdue");

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual("reason.risk.overdue", text);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void The_login_page_renders_its_typed_text(string culture)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var heading = AlvaStrings.Auth_WelcomeToAlva;

            Assert.Contains(heading, Render<Alva.Client.Pages.Login>().Markup);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void A_page_renders_its_typed_heading(string culture)
    {
        Api.Answer("sprints", _ => new[] { new SprintModel { Id = Guid.NewGuid(), Name = "Sprint 7", StartDate = new DateOnly(2026, 11, 2), EndDate = new DateOnly(2026, 11, 20), Status = "Active" } })
           .Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>())
           .Answer("capacity", _ => Array.Empty<CapacityDto>());

        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var heading = AlvaStrings.Sprint_TeamSprints;

            Assert.Contains(heading, Render<Sprints>().Markup);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void An_unknown_key_falls_back_to_itself()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AppStrings>>();

        Assert.Equal("missing.key", localizer["missing.key"].Value);
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
