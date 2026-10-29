using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Один индикатор состояния: правки, сохранение, успех; ошибка не выдаёт себя за успех.</summary>
public sealed class EditorStatusTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Платформа", TeamIds = [] };

    public EditorStatusTests()
    {
        Api.Teams = [new TeamInfo(Api.TeamGuid, "Команда")];
        Api.Answer("projects", _ => new[] { _project })
           .Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
    }

    [Fact]
    public void The_status_reports_dirty_saving_and_saved_but_stays_quiet_when_clean()
    {
        Assert.Contains("Есть несохранённые изменения", Render<EditorStatus>(p => p.Add(x => x.Dirty, true)).Markup);
        Assert.Contains("Сохранение…", Render<EditorStatus>(p => p.Add(x => x.Saving, true)).Markup);
        Assert.Contains("Сохранено", Render<EditorStatus>(p => p.Add(x => x.Saved, true)).Markup);
        Assert.DoesNotContain("Сохранено", Render<EditorStatus>().Markup);
    }

    [Fact]
    public void Saving_a_project_shows_saved_and_the_button_is_disabled_until_it_changes()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-proj-card")));
        Button(cut, "Настройки проекта").Click();

        var save = Button(cut, "Сохранить");
        Assert.True(save.HasAttribute("disabled"));
        Field(cut, "Название").Change("Платформа 2");
        cut.WaitForAssertion(() => Assert.False(Button(cut, "Сохранить").HasAttribute("disabled")));

        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("Сохранено", cut.Markup));
        Button(cut, "Настройки проекта").Click();
        cut.WaitForAssertion(() => Assert.True(Button(cut, "Сохранить").HasAttribute("disabled")));
    }

    [Fact]
    public void A_failed_save_shows_the_error_and_never_says_saved()
    {
        Api.OnSend = (_, _, _) => new HttpRequestException("Сервер недоступен.");
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-proj-card")));
        Button(cut, "Настройки проекта").Click();
        Field(cut, "Название").Change("Платформа 2");

        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("Не удалось связаться с сервером", cut.Find(".flare-alert").TextContent));
        Assert.DoesNotContain("Сервер недоступен.", cut.Markup);
        Assert.DoesNotContain("Сохранено", cut.Markup);
    }
}
