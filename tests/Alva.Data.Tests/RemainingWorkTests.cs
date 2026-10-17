using Alva.Api;
using Alva.Contracts;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

public sealed class RemainingWorkTests
{
    [Fact]
    public async Task Old_edits_preserve_remaining_work_and_explicit_edits_can_clear_it()
    {
        await using var server = await ApiServer.Create(app => app.MapTasks());
        var response = await server.Client.PostAsJsonAsync(server.Team("tasks"), new TaskRequest("Work", RemainingHours: 3, SetRemainingHours: true));
        response.EnsureSuccessStatusCode();
        var task = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal(3m, task.RemainingHours);
        async Task<TaskDto> Edit(TaskRequest request)
        {
            var r = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}"), request);
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<TaskDto>())!;
        }
        Assert.Equal(3m, (await Edit(new("Moved"))).RemainingHours);
        Assert.Equal(0m, (await Edit(new("Finished", RemainingHours: 0, SetRemainingHours: true))).RemainingHours);
        Assert.Null((await Edit(new("Unknown", SetRemainingHours: true))).RemainingHours);
        var invalid = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}"), new TaskRequest("Bad", RemainingHours: -1, SetRemainingHours: true));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
