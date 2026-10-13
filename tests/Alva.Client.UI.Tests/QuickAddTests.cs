using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Пока подзадача создаётся, повторный Enter или клик не создают её второй раз.</summary>
public sealed class QuickAddTests : UiTestBase
{
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };

    public QuickAddTests() => Api.Answer("tasks/", _ => Array.Empty<HistoryDto>());

    private IRenderedComponent<TaskDetails> Details() => Render<TaskDetails>(p => p.Add(x => x.Item, _task));

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<TaskDetails> cut) => cut.Find("input[aria-label='Название новой подзадачи']");

    private int Posts => Api.Sent.Count(s => s.Method == HttpMethod.Post && s.Value is TaskRequest);

    [Fact]
    public void Repeated_enter_and_click_during_a_slow_request_create_one_subtask()
    {
        var reply = new TaskCompletionSource();
        Api.Gate = reply.Task;
        var cut = Details();
        Input(cut).Input("Вёрстка");

        Input(cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Input(cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.True(Button(cut, "Добавить вложенную задачу").HasAttribute("disabled"));
        reply.SetResult();

        cut.WaitForAssertion(() => Assert.True(string.IsNullOrEmpty(Input(cut).GetAttribute("value"))));
        Assert.Equal(1, Posts);
    }

    [Fact]
    public void A_failed_request_keeps_the_title_and_allows_a_retry()
    {
        Api.OnSend = (method, _, _) => method == HttpMethod.Post ? new HttpRequestException("Сервер недоступен") : null;
        var cut = Details();
        Input(cut).Input("Вёрстка");

        Input(cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Contains("Сервер недоступен", cut.Markup));
        Assert.Equal("Вёрстка", Input(cut).GetAttribute("value"));

        Api.OnSend = (_, _, _) => null;
        Button(cut, "Добавить вложенную задачу").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, Posts));
    }
}
