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
            Assert.Equal("Sign out", localizer["Nav_SignOut"].Value);
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Выйти", localizer["Nav_SignOut"].Value);
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
            Assert.Equal("Sign out", AlvaStrings.Nav_SignOut);
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Выйти", AlvaStrings.Nav_SignOut);
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
    public void The_task_tree_section_is_named_in_both_languages()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Task tree", AlvaStrings.Nav_TaskTree);
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Дерево задач", AlvaStrings.Nav_TaskTree);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void Navigation_labels_change_with_the_culture()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Planning", AlvaStrings.Common_Planning);
            Assert.Equal("Tasks", AlvaStrings.Common_Tasks);

            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Планирование", AlvaStrings.Common_Planning);
            Assert.Equal("Задачи", AlvaStrings.Common_Tasks);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void A_page_renders_in_both_languages()
    {
        Api.Answer("sprints", _ => new[] { new SprintModel { Id = Guid.NewGuid(), Name = "Спринт 7", StartDate = new DateOnly(2026, 11, 2), EndDate = new DateOnly(2026, 11, 20), Status = "Active" } })
           .Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>())
           .Answer("capacity", _ => Array.Empty<CapacityDto>());

        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Contains("Team sprints", Render<Sprints>().Markup);

            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Contains("Спринты команды", Render<Sprints>().Markup);
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
