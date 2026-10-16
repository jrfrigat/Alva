using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Contracts;
using Microsoft.JSInterop;
using Xunit;

namespace Alva.Client.UI.Tests;

public sealed class DemoSkillsTests
{
    [Fact]
    public async Task Demo_skills_survive_reload_and_partial_task_updates()
    {
        var storage=new Storage();
        var api=new ApiServiceLocalStorage(storage);
        await api.Initialize();
        await api.Send(HttpMethod.Post,api.TeamPath("actors"),new ActorRequest("Skills person","Person",Skills:[" SQL ","sql"]));
        var actor=(await api.Get<ActorModel[]>(api.TeamPath("actors"))).Single(x=>x.Name=="Skills person");
        var task=await api.Save<TaskModel>(HttpMethod.Post,api.TeamPath("tasks"),new TaskRequest("Skills task",RequiredSkills:["C#"]));
        await api.Send(HttpMethod.Put,api.TeamPath($"tasks/{task.Id}/assignment"),new AssignmentRequest(actor.Id));
        await api.Save<TaskModel>(HttpMethod.Put,api.TeamPath($"tasks/{task.Id}"),new TaskRequest("Updated"));
        var reloaded=new ApiServiceLocalStorage(storage);
        await reloaded.Initialize();
        Assert.Equal(["SQL"],(await reloaded.Get<ActorModel[]>(reloaded.TeamPath("actors"))).Single(x=>x.Id==actor.Id).Skills);
        var saved=(await reloaded.Get<TaskModel[]>(reloaded.TeamPath("tasks"))).Single(x=>x.Id==task.Id);
        Assert.Equal(["C#"],saved.RequiredSkills);
        Assert.Equal(actor.Id,saved.AssignmentTargetId);
        Assert.False(SkillMath.Compatible(saved.RequiredSkills,actor.Skills));
    }

    private sealed class Storage : IJSRuntime
    {
        private readonly Dictionary<string,string> _values=[];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>InvokeAsync<TValue>(identifier,CancellationToken.None,args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken cancellationToken,object?[]? args)
        {
            var key=(string)args![0]!;
            if(identifier=="localStorage.getItem") return ValueTask.FromResult((TValue)(object?)_values.GetValueOrDefault(key)!);
            if(identifier=="localStorage.setItem") _values[key]=(string)args[1]!;
            if(identifier=="localStorage.removeItem") _values.Remove(key);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
