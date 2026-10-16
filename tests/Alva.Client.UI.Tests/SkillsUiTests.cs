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
    public async Task New_task_saves_requirement_tags()
    {
        Api.Answer("tasks", _=>Array.Empty<TaskModel>()).Answer("actors", _=>Array.Empty<ActorModel>())
            .Answer("projects", _=>Array.Empty<ProjectModel>()).Answer("sprints", _=>Array.Empty<SprintModel>());
        Api.SaveResult=new TaskModel { Id=Guid.NewGuid(), Title="New task" };
        var cut=Render<Alva.Client.Pages.Tasks>();
        cut.WaitForAssertion(()=>Button(cut,"Новая задача"));
        Button(cut,"Новая задача").Click();
        Field(cut,"Название").Change("New task");
        await cut.InvokeAsync(()=>cut.FindComponent<SkillTagEditor>().Instance.ValuesChanged.InvokeAsync(["SQL"]));
        Button(cut,"Сохранить задачу").Click();
        cut.WaitForAssertion(()=>Assert.Contains(Api.Sent,x=>x.Method==HttpMethod.Post && x.Value is TaskRequest r && r.RequiredSkills!.SequenceEqual(new[]{"SQL"})));
    }

    [Fact]
    public async Task Tags_are_editable_and_normalized_without_case_duplicates()
    {
        string[] selected = [];
        var cut = Render<SkillTagEditor>(p=>p.Add(x=>x.Label,"Навыки").Add(x=>x.ValuesChanged,(string[] values)=>selected=values));
        await cut.InvokeAsync(()=>cut.FindComponent<FlareTagField<string>>().Instance.ValuesChanged.InvokeAsync([" SQL ", "sql", "C#"]));
        Assert.Equal(["SQL", "C#"], selected);
    }

    [Fact]
    public async Task Requirement_changes_are_dirty_and_saved()
    {
        var task = new TaskModel { Id=Guid.NewGuid(), Title="Task", Version=1 };
        Api.Answer("tasks/", _=>Array.Empty<HistoryDto>());
        var cut=Render<TaskDetails>(p=>p.Add(x=>x.Item,task));
        await cut.InvokeAsync(()=>cut.FindComponent<SkillTagEditor>().Instance.ValuesChanged.InvokeAsync(["SQL"]));
        Assert.True(cut.Instance.Dirty);
        Button(cut,"Сохранить").Click();
        cut.WaitForAssertion(()=>Assert.Contains(Api.Sent,x=>x.Value is TaskRequest r && r.RequiredSkills!.SequenceEqual(new[]{"SQL"})));
    }

    [Fact]
    public async Task Mismatched_manual_assignment_succeeds_and_shows_missing_skills()
    {
        var person=new ActorModel {Id=Guid.NewGuid(),Name="Person",Skills=["CSS"]};
        var task=new TaskModel {Id=Guid.NewGuid(),Title="Database",AssignmentTargetId=person.Id,RequiredSkills=["SQL"]};
        Api.Answer("tasks",_=>new[]{task}).Answer("actors",_=>new[]{person});
        var session=Services.GetRequiredService<AlvaSession>();
        var banner=Render<SkillAssignmentNotice>();
        await banner.InvokeAsync(()=>session.Send(HttpMethod.Put,session.TeamPath($"tasks/{task.Id}/assignment"),new AssignmentRequest(person.Id)));
        Assert.Single(Api.Sent);
        banner.WaitForAssertion(()=>Assert.Contains("SQL",banner.Markup));
        Assert.Contains("Не хватает навыков:",session.AssignmentNotice);
        person.Skills=["sql"];
        await banner.InvokeAsync(()=>session.Send(HttpMethod.Put,session.TeamPath($"tasks/{task.Id}/assignment"),new AssignmentRequest(person.Id)));
        Assert.Null(session.AssignmentNotice);
    }

    [Fact]
    public void Existing_assignment_warning_recalculates_after_skill_changes()
    {
        var person=new ActorModel {Id=Guid.NewGuid(),Name="Person"};
        var task=new TaskModel {AssignmentTargetId=person.Id,RequiredSkills=["SQL"]};
        var cut=Render<AlvaAssignee>(p=>p.Add(x=>x.Task,task).Add(x=>x.Actors,new[]{person}));
        Assert.Contains("SQL",cut.Markup);
        person.Skills=["sql"];
        cut.Render(p=>p.Add(x=>x.Task,task).Add(x=>x.Actors,new[]{person}));
        Assert.Contains("Все необходимые навыки есть",cut.Markup);
        Assert.DoesNotContain("Не хватает навыков:",cut.Markup);
    }
}
