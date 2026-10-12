using Alva.Client.Models;
using Alva.Contracts;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Flare.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>TASK-64: фокус следует за страницей, которая догрузилась, и за открытым и закрытым редактором.</summary>
public sealed class FocusTests : UiTestBase
{
    private readonly SprintModel _sprint = new() { Id = Guid.NewGuid(), Name = "Спринт 7", Status = "Active", StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 16) };
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned" };

    public FocusTests()
    {
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => new[] { _sprint })
           .Answer("tasks/", _ => Array.Empty<HistoryDto>());
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    // Элементы, на которые по очереди переводили фокус: ElementReference сверяется с разметкой.
    private static string[] Focused<T>(IRenderedComponent<T> cut, BunitJSInterop js) where T : IComponent =>
        js.Invocations.Where(i => i.Identifier.EndsWith("focus", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Arguments.OfType<ElementReference>().First().Id)
            .Select(id => cut.FindAll($"[blazor\\:elementReference='{id}']").FirstOrDefault() is { } e ? Describe(e) : "(нет на странице)")
            .ToArray();

    private static string Describe(AngleSharp.Dom.IElement element) =>
        element.TagName == "BUTTON" ? "button:" + element.TextContent.Trim() : element.TagName.ToLowerInvariant() + ":" + (element.GetAttribute("id") ?? element.TextContent.Trim());

    [Fact]
    public void A_task_page_moves_focus_to_its_heading_once_it_has_loaded()
    {
        Navigation.NavigateTo($"/tasks/{_task.Id}");
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<Tasks>(t => t.Add(x => x.TaskId, _task.Id)));

        cut.WaitForAssertion(() => Assert.Equal(["h1:" + cut.Find("h1").TextContent.Trim()], Focused(cut, JSInterop)));
        Assert.Equal("-1", cut.Find("h1").GetAttribute("tabindex"));

        // Обычное обновление страницы фокус не трогает.
        cut.Render();
        Assert.Single(Focused(cut, JSInterop));
    }

    [Fact]
    public void A_sprint_page_moves_focus_to_its_heading_once_it_has_loaded()
    {
        Navigation.NavigateTo($"/sprints/{_sprint.Id}");
        var cut = Render<Sprints>(p => p.Add(x => x.SprintId, _sprint.Id));

        cut.WaitForAssertion(() => Assert.Equal(["h1:Спринт 7"], Focused(cut, JSInterop)));
    }


    // bunit пишет id ссылки кнопки только в разметку её первого рендера, поэтому он запоминается до нажатия.
    private static string RefOf(AngleSharp.Dom.IElement element) => element.GetAttribute("blazor:elementReference") is { Length: > 0 } id ? id : throw new InvalidOperationException("У элемента нет ElementReference");

    private string LastFocusedRef() => JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus", StringComparison.OrdinalIgnoreCase)).Arguments.OfType<ElementReference>().First().Id;

    [Fact]
    public void The_new_task_editor_takes_focus_and_gives_it_back_when_closed()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Button(cut, "Новая задача"));
        var opener = RefOf(Button(cut, "Новая задача"));

        Button(cut, "Новая задача").Click();
        var title = Field(cut, "Название").Id!;
        Assert.Equal($"input:{title}", Focused(cut, JSInterop).Last());

        Button(cut, "Закрыть").Click();
        Assert.Equal(opener, LastFocusedRef());
    }

    [Fact]
    public void The_new_sprint_editor_takes_focus_and_gives_it_back_when_closed()
    {
        Navigation.NavigateTo("/sprints");
        var cut = Render<Sprints>();
        cut.WaitForAssertion(() => Button(cut, "Новый спринт"));
        var opener = RefOf(Button(cut, "Новый спринт"));

        Button(cut, "Новый спринт").Click();
        Assert.Equal($"input:{Field(cut, "Название").Id}", Focused(cut, JSInterop).Last());

        Button(cut, "Закрыть").Click();
        Assert.Equal(opener, LastFocusedRef());
    }
}
