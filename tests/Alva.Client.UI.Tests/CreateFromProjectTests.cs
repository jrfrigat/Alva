using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>«Создать задачу» в проекте открывает форму с этим проектом и после сохранения возвращает к нему.</summary>
public sealed class CreateFromProjectTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Платформа" };

    public CreateFromProjectTests()
    {
        Api.Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("projects", _ => new[] { _project })
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
        Api.SaveResult = new TaskModel { Id = Guid.NewGuid(), Title = "Новая" };
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void The_form_opens_with_the_project_and_saving_returns_to_it()
    {
        Navigation.NavigateTo($"tasks?new=1&project={_project.Id}&back=project");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Contains("Платформа", cut.FindAll(".flare-select").First(s => s.TextContent.Contains("Проект") && !s.TextContent.Contains("Все проекты")).TextContent));

        Field(cut, "Название").Input("Новая");
        Button(cut, "Сохранить задачу").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"/projects?project={_project.Id}", Navigation.Uri));
        var request = Assert.IsType<TaskRequest>(Api.Sent.First(s => s.Method == HttpMethod.Post).Value);
        Assert.Equal(_project.Id, request.ProjectId);
    }
}
