using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Карточка задачи: фиксированные даты блокируют перенос и уходят в запрос явно.</summary>
public sealed class TaskDetailsFixedDatesTests : UiTestBase
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    private static TaskModel Task(bool datesFixed, bool dated = true) => new()
    {
        Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1, DatesFixed = datesFixed,
        StartDate = dated ? Start : null, EndDate = dated ? Start.AddDays(2) : null
    };

    private IRenderedComponent<FlareConfirmDialogProvider> Page(TaskModel task)
    {
        Api.Answer($"tasks/{task.Id}/history", _ => Array.Empty<HistoryDto>());
        return Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d.Add(x => x.Item, task)));
    }

    private static AngleSharp.Dom.IElement FixedSwitch<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.Find("input[role='switch']");

    [Fact]
    public void Fixed_dates_disable_the_date_fields()
    {
        var cut = Page(Task(datesFixed: true));

        Assert.False(FixedSwitch(cut).HasAttribute("disabled"));
        Assert.Equal("true", FixedSwitch(cut).GetAttribute("aria-checked"));
        Assert.True(Field(cut, "Начало").HasAttribute("disabled"));
        Assert.True(Field(cut, "Окончание").HasAttribute("disabled"));
        Assert.Contains("Даты зафиксированы", cut.Markup);
    }

    [Fact]
    public void A_task_without_dates_cannot_be_fixed()
    {
        var cut = Page(Task(datesFixed: false, dated: false));

        Assert.True(FixedSwitch(cut).HasAttribute("disabled"));
        Assert.Contains("Сначала задайте начало и окончание", cut.Markup);
    }

    [Fact]
    public void Unfixing_is_dirty_and_saved_explicitly()
    {
        var task = Task(datesFixed: true);
        var cut = Page(task);

        FixedSwitch(cut).Change(false);
        Assert.True(cut.FindComponent<TaskDetails>().Instance.Dirty);

        TaskRequest? sent = null;
        Api.OnSend = (_, _, value) => { sent = value as TaskRequest; return null; };
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.False(sent!.DatesFixed);
        Assert.False(Field(cut, "Начало").HasAttribute("disabled"));
    }
}
