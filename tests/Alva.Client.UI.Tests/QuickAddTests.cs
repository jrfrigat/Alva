using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>While a subtask is being created, a repeated Enter or click does not create it a second time.</summary>
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
    public void A_quick_subtask_can_be_created_with_an_estimate()
    {
        var cut = Details();
        Input(cut).Input("Вёрстка");
        cut.Find("input[aria-label='Оценка, часы']").Change("8");

        Input(cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal(1, Posts));
        var request = Assert.IsType<TaskRequest>(Api.Sent.Single(x => x.Method == HttpMethod.Post).Value);
        Assert.Equal(8m, request.EstimateHours);
    }

    [Fact]
    public void A_subtask_estimate_can_be_changed_in_place()
    {
        var child = new TaskModel { Id = Guid.NewGuid(), ParentId = _task.Id, Title = "Вёрстка", Status = "Backlog", Version = 3 };
        var cut = Render<TaskDetails>(p => p.Add(x => x.Item, _task).Add(x => x.Tasks, new[] { _task, child }));
        TaskTabs(cut).QuerySelectorAll("[role=tab]").Single(x => x.TextContent.Trim() == "Подзадачи").Click();

        cut.Find($"input[aria-label='Оценка: Вёрстка']").Change("6");

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, x =>
            x.Method == HttpMethod.Put && x.Path.EndsWith($"tasks/{child.Id}", StringComparison.Ordinal)
            && x.Value is TaskRequest { EstimateHours: 6m }));
    }

    [Fact]
    public void A_failed_request_keeps_the_title_and_allows_a_retry()
    {
        Api.OnSend = (method, _, _) => method == HttpMethod.Post ? new HttpRequestException("Сервер недоступен") : null;
        var cut = Details();
        Input(cut).Input("Вёрстка");

        Input(cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Contains("Не удалось связаться с сервером", cut.Markup));
        Assert.Equal("Вёрстка", Input(cut).GetAttribute("value"));

        Api.OnSend = (_, _, _) => null;
        Button(cut, "Добавить вложенную задачу").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, Posts));
    }
}
