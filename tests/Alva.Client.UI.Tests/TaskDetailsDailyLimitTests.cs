using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Карточка задачи: лимит часов в день подсказывает нужные дни и уходит в запрос явно.</summary>
public sealed class TaskDetailsDailyLimitTests : UiTestBase
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private IRenderedComponent<FlareConfirmDialogProvider> Page(TaskModel task)
    {
        Api.Answer($"tasks/{task.Id}/history", _ => Array.Empty<HistoryDto>());
        return Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d.Add(x => x.Item, task)));
    }

    [Fact]
    public void A_short_window_is_explained_and_the_limit_is_saved()
    {
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Отчёт", Status = "Planned", Version = 1, EstimateHours = 10, StartDate = Monday, EndDate = Monday.AddDays(2) };
        var cut = Page(task);

        Field(cut, "Не больше, ч в день").Change("2");

        Assert.Contains("нужно 5", cut.Markup);
        Assert.Contains("работа выйдет за окончание", cut.Markup);
        Assert.True(cut.FindComponent<TaskDetails>().Instance.Dirty);
        TaskRequest? sent = null;
        Api.OnSend = (_, _, value) => { sent = value as TaskRequest; return null; };
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal(2m, sent!.DailyHoursLimit);
        Assert.True(sent.SetDailyHoursLimit);
    }

    [Fact]
    public void A_zero_limit_is_refused_before_sending()
    {
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Отчёт", Status = "Planned", Version = 1 };
        var cut = Page(task);
        var sent = false;
        Api.OnSend = (_, _, _) => { sent = true; return null; };

        Field(cut, "Не больше, ч в день").Change("0");
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("больше 0 и не больше 24 часов", cut.Markup));
        Assert.False(sent);
    }
}
