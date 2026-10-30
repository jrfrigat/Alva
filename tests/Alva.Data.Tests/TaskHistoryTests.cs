using System.Net;
using System.Net.Http.Json;
using Alva.Api;
using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>История задачи: отдаётся и на SQLite, новые записи — первыми.</summary>
public sealed class TaskHistoryTests
{
    [Fact]
    public async Task History_is_served_newest_first_on_sqlite()
    {
        await using var server = await ApiServer.Create(app => app.MapTasks());
        var created = await server.Client.PostAsJsonAsync(server.Team("tasks"), new TaskRequest("Отчёт"));
        var task = (await created.Content.ReadFromJsonAsync<TaskDto>())!;
        await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}"), new TaskRequest("Отчёт за месяц", Version: task.Version));

        var response = await server.Client.GetAsync(server.Team($"tasks/{task.Id}/history"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await response.Content.ReadFromJsonAsync<HistoryDto[]>())!;
        Assert.Equal(["Задача обновлена", "Задача создана"], history.Select(h => h.Summary));
        Assert.True(history[0].At >= history[1].At);
    }
}
