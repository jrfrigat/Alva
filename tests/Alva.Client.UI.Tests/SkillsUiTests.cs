using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

public sealed class SkillsUiTests : UiTestBase
{
    [Fact]
    public async Task New_task_saves_the_selected_skill_identifiers()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "SQL", Kind = "Skill" };
        Api.Answer("tasks", _ => Array.Empty<TaskModel>()).Answer("actors", _ => new[] { skill })
            .Answer("projects", _ => Array.Empty<ProjectModel>()).Answer("sprints", _ => Array.Empty<SprintModel>());
        Api.SaveResult = new TaskModel { Id = Guid.NewGuid(), Title = "New task" };
        var cut = Render<Alva.Client.Pages.Tasks>();
        cut.WaitForAssertion(() => Button(cut, "Новая задача"));
        Button(cut, "Новая задача").Click();
        Button(cut, "Больше полей").Click();
        Field(cut, "Название").Change("New task");
        await cut.InvokeAsync(() => cut.FindComponent<FlareMultiSelect<Guid>>().Instance.ValuesChanged.InvokeAsync([skill.Id]));
        Button(cut, "Сохранить задачу").Click();
        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, x => x.Method == HttpMethod.Post && x.Value is TaskRequest r && r.RequiredSkillIds!.SequenceEqual(new[] { skill.Id })));
    }

    [Fact]
    public async Task Requirement_changes_are_dirty_and_saved()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "SQL", Kind = "Skill" };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Task", Version = 1 };
        Api.Answer("actors", _ => new[] { skill }).Answer("tasks/", _ => Array.Empty<HistoryDto>());
        var cut = Render<TaskDetails>(p => p.Add(x => x.Item, task));
        await cut.InvokeAsync(() => cut.FindComponent<FlareMultiSelect<Guid>>().Instance.ValuesChanged.InvokeAsync([skill.Id]));
        Assert.True(cut.Instance.Dirty);
        Button(cut, "Сохранить").Click();
        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, x => x.Value is TaskRequest r && r.RequiredSkillIds!.SequenceEqual(new[] { skill.Id })));
    }

    [Fact]
    public async Task Mismatched_manual_assignment_succeeds_and_shows_missing_skills()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "SQL", Kind = "Skill" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Person" };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Database", AssignmentTargetId = person.Id, RequiredSkillIds = [skill.Id] };
        Api.Answer("tasks", _ => new[] { task }).Answer("actors", _ => new[] { person, skill });
        var session = Services.GetRequiredService<AlvaSession>();
        var banner = Render<SkillAssignmentNotice>();
        await banner.InvokeAsync(() => session.Send(HttpMethod.Put, session.TeamPath($"tasks/{task.Id}/assignment"), new AssignmentRequest(person.Id)));
        Assert.Single(Api.Sent);
        banner.WaitForAssertion(() => Assert.Contains("SQL", banner.Markup));
        Assert.Contains("Не хватает навыков:", session.AssignmentNotice);
        person.SkillIds = [skill.Id];
        await banner.InvokeAsync(() => session.Send(HttpMethod.Put, session.TeamPath($"tasks/{task.Id}/assignment"), new AssignmentRequest(person.Id)));
        Assert.Null(session.AssignmentNotice);
    }

    [Fact]
    public void Existing_assignment_warning_disappears_after_skill_changes()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "SQL", Kind = "Skill" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Person" };
        var task = new TaskModel { AssignmentTargetId = person.Id, RequiredSkillIds = [skill.Id] };
        var actors = new[] { person, skill };
        var cut = Render<AlvaAssignee>(p => p.Add(x => x.Task, task).Add(x => x.Actors, actors));
        Assert.Contains("SQL", cut.Markup);
        person.SkillIds = [skill.Id];
        cut.Render(p => p.Add(x => x.Task, task).Add(x => x.Actors, actors));
        Assert.DoesNotContain("Все необходимые навыки есть", cut.Markup);
        Assert.DoesNotContain("Не хватает навыков:", cut.Markup);
    }

    [Fact]
    public void The_sprint_issues_panel_lists_only_tasks_with_missing_skills()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "SQL", Kind = "Skill" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var task = new TaskModel
        {
            Id = Guid.NewGuid(), Title = "База", AssignmentTargetId = person.Id, RequiredSkillIds = [skill.Id],
            EstimateHours = 8, EffectiveEstimateHours = 8, StartDate = new DateOnly(2026, 11, 9), EndDate = new DateOnly(2026, 11, 10)
        };
        var actors = new[] { person, skill };
        var data = GanttData.Create([task], [], actors, [], []);
        var cut = Render<PlanIssues>(p => p.Add(x => x.Data, data).Add(x => x.Tasks, new[] { task }).Add(x => x.Actors, actors));

        Assert.Contains("База", cut.Markup);
        Assert.Contains("SQL", cut.Markup);

        person.SkillIds = [skill.Id];
        cut.Render(p => p.Add(x => x.Data, data).Add(x => x.Tasks, new[] { task }).Add(x => x.Actors, actors));
        Assert.DoesNotContain("База", cut.Markup);
    }

    [Fact]
    public void The_sprint_issues_panel_lists_a_task_without_an_estimate()
    {
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var task = new TaskModel
        {
            Id = Guid.NewGuid(), Title = "База", AssignmentTargetId = person.Id, EffectiveEstimateHours = 0,
            StartDate = new DateOnly(2026, 11, 9), EndDate = new DateOnly(2026, 11, 10)
        };
        var data = GanttData.Create([task], [], [person], [], []);
        var cut = Render<PlanIssues>(p => p.Add(x => x.Data, data).Add(x => x.Tasks, new[] { task }).Add(x => x.Actors, new[] { person }));

        Assert.Contains("База", cut.Markup);
        Assert.Contains("Нет оценки", cut.Markup);
    }
}
