using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using AngleSharp.Dom;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Операции с вычетами и периодами не сбрасывают несохранённые часы недели и часовой пояс.</summary>
public sealed class ScheduleEditorTests : UiTestBase
{
    private readonly Guid _person = Guid.NewGuid();
    private CalendarDto _calendar;

    public ScheduleEditorTests()
    {
        _calendar = new CalendarDto(_person, "Europe/Moscow", 8, 8, 8, 8, 8, 0, 0,
            [new CalendarExceptionDto(Guid.NewGuid(), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31), 0, "Праздник")],
            [new RecurringBlockDto(Guid.NewGuid(), 1, 1, "Планёрка")]);
        Api.Answer("actors", _ => new[] { new ActorModel { Id = _person, Name = "Анна Петрова" } })
           .Answer($"actors/{_person}/calendar", _ => _calendar)
           .Answer("capacity", _ => Array.Empty<CapacityDto>());
        // Сервер меняет только списки; часы недели меняет лишь PUT календаря.
        Api.OnSend = (method, path, value) =>
        {
            _calendar = value switch
            {
                RecurringBlockRequest b => _calendar with { RecurringBlocks = [.. _calendar.RecurringBlocks, new RecurringBlockDto(Guid.NewGuid(), b.DayOfWeek, b.Hours, b.Name)] },
                CalendarExceptionRequest e => _calendar with { Exceptions = [.. _calendar.Exceptions, new CalendarExceptionDto(Guid.NewGuid(), e.From, e.To, e.AvailableHours, e.Reason)] },
                CalendarRequest p => _calendar with { TimeZone = p.TimeZone, Monday = p.Monday, Tuesday = p.Tuesday, Wednesday = p.Wednesday, Thursday = p.Thursday, Friday = p.Friday, Saturday = p.Saturday, Sunday = p.Sunday },
                _ when method == HttpMethod.Delete && path.Contains("/blocks/") => _calendar with { RecurringBlocks = [] },
                _ when method == HttpMethod.Delete && path.Contains("/exceptions/") => _calendar with { Exceptions = [] },
                _ => _calendar
            };
            return null;
        };
    }

    private IRenderedComponent<ScheduleEditor> Editor() => Render<ScheduleEditor>(p => p.Add(x => x.ActorId, _person));

    private static IElement Monday(IRenderedComponent<ScheduleEditor> cut) => cut.Find("input[aria-label='Часы: Понедельник']");
    private static IElement SaveButton(IRenderedComponent<ScheduleEditor> cut) => Button(cut, "Сохранить график");

    // Пользователь меняет часы понедельника и пояс, но не сохраняет.
    private static void EditDraft(IRenderedComponent<ScheduleEditor> cut)
    {
        Monday(cut).Change("6");
        Field(cut, "Часовой пояс IANA").Change("Asia/Tokyo");
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
    }

    private static void AssertDraftKept(IRenderedComponent<ScheduleEditor> cut)
    {
        Assert.Equal("6", Monday(cut).GetAttribute("value"));
        Assert.Equal("Asia/Tokyo", Field(cut, "Часовой пояс IANA").GetAttribute("value"));
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
    }

    // Узкая раскладка показывает день карточкой: подписи в ячейках должны совпадать с заголовками колонок,
    // иначе на телефоне величины читаются без имени — строка заголовков там скрыта.
    [Fact]
    public void Every_day_row_labels_its_values_so_the_narrow_layout_stays_readable()
    {
        var cut = Editor();

        var headers = cut.FindAll(".alva-week__row--head [role='columnheader']").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal(["День недели", "Рабочий", "Часов в день", "Вычеты", "Чистые часы"], headers);

        var labels = cut.FindAll(".alva-week__row:not(.alva-week__row--head) .alva-week__label").Select(l => l.TextContent.Trim()).ToArray();
        Assert.Equal(7 * 4, labels.Length);
        Assert.Equal(headers.Skip(1), labels.Distinct());
    }

    [Fact]
    public void Adding_a_recurring_block_keeps_the_unsaved_week()
    {
        var cut = Editor();
        EditDraft(cut);
        Field(cut, "Встреча или интервал").Input("Ретро");
        Button(cut, "Добавить нерабочий интервал").Click();
        cut.WaitForAssertion(() => Assert.Contains("Ретро", cut.Markup));
        AssertDraftKept(cut);
    }

    [Fact]
    public void Adding_an_absence_keeps_the_unsaved_week()
    {
        var cut = Editor();
        EditDraft(cut);
        Field(cut, "С").Change("02.11.2026");
        Field(cut, "По").Change("03.11.2026");
        Field(cut, "Причина").Input("Отпуск");
        Button(cut, "Добавить период").Click();
        cut.WaitForAssertion(() => Assert.Contains("Отпуск", cut.Markup));
        AssertDraftKept(cut);
    }

    [Fact]
    public void Deleting_a_recurring_block_keeps_the_unsaved_week()
    {
        var cut = Editor();
        EditDraft(cut);
        Button(cut, "Удалить вычет «Планёрка»").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Планёрка", cut.Markup));
        AssertDraftKept(cut);
    }

    [Fact]
    public void Deleting_an_absence_keeps_the_unsaved_week()
    {
        var cut = Editor();
        EditDraft(cut);
        Button(cut, "Удалить «Праздник»").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Праздник", cut.Markup));
        AssertDraftKept(cut);
    }

    [Fact]
    public void A_failed_request_keeps_the_unsaved_week_and_shows_the_error()
    {
        var cut = Editor();
        EditDraft(cut);
        Api.OnSend = (_, _, _) => new HttpRequestException("Сервер недоступен");
        Button(cut, "Удалить вычет «Планёрка»").Click();
        cut.WaitForAssertion(() => Assert.Contains("Не удалось связаться с сервером", cut.Markup));
        AssertDraftKept(cut);
    }

    [Fact]
    public void Deleting_a_block_asks_before_removing_it()
    {
        var page = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<ScheduleEditor>(e => e.Add(x => x.ActorId, _person)));
        page.WaitForAssertion(() => Assert.Contains("Планёрка", page.Markup));

        Button(page, "Удалить вычет «Планёрка»").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[role='alertdialog']")));
        Button(page, "Отмена").Click();
        Assert.Contains("Планёрка", page.Markup);
    }

    [Fact]
    public void Resetting_asks_before_discarding_the_draft()
    {
        var page = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<ScheduleEditor>(e => e.Add(x => x.ActorId, _person)));
        page.Find("input[aria-label='Часы: Понедельник']").Change("6");

        Button(page, "Сбросить изменения").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[role='alertdialog']")));
        Button(page, "Остаться").Click();
        Assert.Equal("6", page.Find("input[aria-label='Часы: Понедельник']").GetAttribute("value"));
    }

    [Fact]
    public void Saving_takes_the_draft_as_the_saved_week()
    {
        var cut = Editor();
        EditDraft(cut);
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => Assert.True(SaveButton(cut).HasAttribute("disabled")));
        Assert.Equal(6, _calendar.Monday);
        Assert.Equal("Asia/Tokyo", _calendar.TimeZone);
    }
}
