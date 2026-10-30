using Alva.Api;
using Alva.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Лимит часов в день через API: сохраняется явно, служебные правки его не стирают, неверное значение отклоняется.</summary>
public sealed class TaskDailyLimitApiTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static Task<ApiServer> Server() => ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); app.MapActors(); app.MapBalance(); });

    [Fact]
    public async Task The_limit_is_saved_kept_by_plain_edits_and_cleared_explicitly()
    {
        await using var server = await Server();
        var created = await server.Client.PostAsJsonAsync(server.Team("tasks"), new TaskRequest("Отчёт", EstimateHours: 10,
            StartDate: Monday, EndDate: Monday.AddDays(4), DailyHoursLimit: 2, SetDailyHoursLimit: true));
        var task = (await created.Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal(2m, task.DailyHoursLimit);

        // Перенос с Гантта не знает о лимите и не должен его снять.
        var moved = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}"), new TaskRequest("Отчёт", EstimateHours: 10,
            StartDate: Monday.AddDays(1), EndDate: Monday.AddDays(5), Version: task.Version));
        Assert.Equal(2m, (await moved.Content.ReadFromJsonAsync<TaskDto>())!.DailyHoursLimit);

        var cleared = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}"), new TaskRequest("Отчёт", EstimateHours: 10,
            StartDate: Monday.AddDays(1), EndDate: Monday.AddDays(5), SetDailyHoursLimit: true));
        Assert.Null((await cleared.Content.ReadFromJsonAsync<TaskDto>())!.DailyHoursLimit);
        await server.WithDb(async db =>
            Assert.Contains(await db.TaskHistory.Where(h => h.TaskId == task.Id).Select(h => h.Summary).ToListAsync(), s => s == "Лимит в день снят"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public async Task A_limit_outside_one_day_is_rejected(decimal limit)
    {
        await using var server = await Server();

        var response = await server.Client.PostAsJsonAsync(server.Team("tasks"), new TaskRequest("Отчёт", DailyHoursLimit: limit, SetDailyHoursLimit: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
