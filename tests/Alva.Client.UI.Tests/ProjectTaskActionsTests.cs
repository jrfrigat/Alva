using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Tasks, subtasks and typed links are created on the project page without leaving it.</summary>
public sealed class ProjectTaskActionsTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Портал", TeamIds = [] };
    private readonly TaskModel _first;
    private readonly TaskModel _second;
    private readonly Guid _savedId = Guid.NewGuid();

    public ProjectTaskActionsTests()
    {
        _project.TeamIds = [Api.TeamGuid];
        _first = new TaskModel { Id = Guid.NewGuid(), ProjectId = _project.Id, Title = "План", Status = "Planned", Order = 0 };
        _second = new TaskModel { Id = Guid.NewGuid(), ProjectId = _project.Id, Title = "Анонс", Status = "Planned", Order = 1 };
        Api.Answer("projects", _ => new[] { _project })
           .Answer("tasks", _ => new[] { _first, _second })
           .Answer("tasks/dependencies", _ => Array.Empty<DependencyDto>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
        Api.SaveResult = new TaskModel { Id = _savedId, Title = "Новая", Status = "Backlog", ProjectId = _project.Id };
    }

    [Fact]
    public void A_task_is_created_with_the_project_without_leaving_the_page()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("План", cut.Markup));

        Button(cut, "Создать задачу").Click();
        Field(cut, "Название").Change("Новая задача проекта");
        ClickExact(cut, "Создать");

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Post
            && s.Value is TaskRequest r && r.Title == "Новая задача проекта" && r.ProjectId == _project.Id && r.ParentId is null));
    }

    [Fact]
    public void A_subtask_is_created_with_the_parent_from_the_task_row()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("План", cut.Markup));

        cut.Find(".alva-wbs").QuerySelector("[aria-label='Добавить подзадачу: План']")!.Click();
        Field(cut, "Название").Change("Подзадача плана");
        ClickExact(cut, "Создать");

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Post
            && s.Value is TaskRequest r && r.Title == "Подзадача плана" && r.ProjectId == _project.Id && r.ParentId == _first.Id));
    }

    [Theory]
    [InlineData("FS", "Окончание -> начало", 2)]
    [InlineData("SS", "Начало -> начало", 3)]
    [InlineData("FF", "Окончание -> окончание", -1)]
    [InlineData("SF", "Начало -> окончание", 5)]
    public void A_typed_link_is_created_with_direction_type_and_lag(string kind, string label, int lag)
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("План", cut.Markup));

        cut.Find(".alva-wbs").QuerySelector("[aria-label='Добавить связь: План']")!.Click();
        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача"),
            $"{AlvaFormat.Code(_second.Id)} {_second.Title}");
        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Вид"), label);
        Field(cut, "Лаг, дни").Change(lag.ToString());
        Dialog(cut).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Добавить связь").Click();

        cut.WaitForAssertion(() =>
        {
            var sent = Assert.Single(Api.Sent, s => s.Method == HttpMethod.Post);
            Assert.EndsWith($"tasks/{_first.Id}/dependencies", sent.Path);
            Assert.Equal(new DependencyRequest(_second.Id, _first.Id, kind, lag), sent.Value);
        });
    }

    [Fact]
    public void The_second_task_cannot_be_the_task_itself()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("План", cut.Markup));

        cut.Find(".alva-wbs").QuerySelector("[aria-label='Добавить связь: План']")!.Click();

        var candidates = cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача").Instance.Items!;
        Assert.DoesNotContain(_first.Id, candidates);
        Assert.Contains(_second.Id, candidates);
    }

    [Fact]
    public void A_link_that_would_close_a_cycle_is_not_sent()
    {
        Api.Answer("tasks/dependencies", _ => new[] { new DependencyDto(Guid.NewGuid(), _first.Id, _second.Id, "FS", 0) });
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("План", cut.Markup));

        cut.Find(".alva-wbs").QuerySelector("[aria-label='Добавить связь: Анонс']")!.Click();
        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача"),
            $"{AlvaFormat.Code(_first.Id)} {_first.Title}");
        Choose(cut.FindComponents<FlareSelect<bool>>().Single(), "Последователь - ждёт эту задачу");
        Dialog(cut).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Добавить связь").Click();

        cut.WaitForAssertion(() => Assert.Contains("Связь создаст цикл.", Dialog(cut).TextContent));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_duplicate_link_is_not_sent()
    {
        Api.Answer("tasks/dependencies", _ => new[] { new DependencyDto(Guid.NewGuid(), _first.Id, _second.Id, "FS", 0) });
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("План", cut.Markup));

        cut.Find(".alva-wbs").QuerySelector("[aria-label='Добавить связь: Анонс']")!.Click();
        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Задача"),
            $"{AlvaFormat.Code(_first.Id)} {_first.Title}");
        Dialog(cut).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Добавить связь").Click();

        cut.WaitForAssertion(() => Assert.Contains("Такая связь уже есть.", Dialog(cut).TextContent));
        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Post);
    }

    private static AngleSharp.Dom.IElement Dialog<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("[role='dialog']").Last();

    private static void ClickExact<T>(IRenderedComponent<T> cut, string text) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    private static void Choose<T>(IRenderedComponent<FlareSelect<T>> select, string option)
    {
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(e => e.TextContent.Trim() == option).Click();
    }
}
