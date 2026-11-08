using Alva.Api;
using Alva.Contracts;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Справочник участников работает и на локальной SQLite, где нет SELECT … FOR UPDATE.</summary>
public sealed class SqliteActorTests
{
    [Fact]
    public async Task A_person_can_be_created_changed_and_archived_on_sqlite()
    {
        var server = await ApiServer.Create(app => app.MapActors());
        await using var _ = server;

        var created = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Анна", "Person"));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var actor = (await created.Content.ReadFromJsonAsync<ActorDto>())!;

        var changed = await server.Client.PutAsJsonAsync(server.Team($"actors/{actor.Id}"), new ActorRequest("Анна Ковалёва", "Person"));
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());

        var archived = await server.Client.DeleteAsync(server.Team($"actors/{actor.Id}"));
        Assert.True(archived.IsSuccessStatusCode, await archived.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Skills_are_a_dictionary_and_cannot_be_assignment_targets()
    {
        var server = await ApiServer.Create(app => { app.MapActors(); app.MapTasks(); });
        await using var _ = server;

        var skillResponse = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Backend", "Skill"));
        Assert.True(skillResponse.IsSuccessStatusCode, await skillResponse.Content.ReadAsStringAsync());
        var skill = (await skillResponse.Content.ReadFromJsonAsync<ActorDto>())!;

        var personResponse = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Анна", "Person", SkillIds: [skill.Id]));
        Assert.True(personResponse.IsSuccessStatusCode, await personResponse.Content.ReadAsStringAsync());
        var person = (await personResponse.Content.ReadFromJsonAsync<ActorDto>())!;
        Assert.Contains(skill.Id, person.SkillIds!);

        var taskResponse = await server.Client.PostAsJsonAsync(server.Team("tasks"), new TaskRequest("Задача"));
        Assert.True(taskResponse.IsSuccessStatusCode, await taskResponse.Content.ReadAsStringAsync());
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDto>())!;

        var assigned = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}/assignment"), new AssignmentRequest(skill.Id));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, assigned.StatusCode);
    }

    [Fact]
    public async Task Membership_periods_are_validated_and_the_old_field_still_works()
    {
        var server = await ApiServer.Create(app => app.MapActors());
        await using var _ = server;
        var departmentResponse = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Разработка", "Department"));
        var department = (await departmentResponse.Content.ReadFromJsonAsync<ActorDto>())!;
        var first = new DepartmentMembershipRequest(null, department.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var second = new DepartmentMembershipRequest(null, department.Id, new DateOnly(2026, 7, 1), null);
        var personResponse = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Анна", "Person", DepartmentMemberships: [first, second]));
        Assert.True(personResponse.IsSuccessStatusCode, await personResponse.Content.ReadAsStringAsync());
        var person = (await personResponse.Content.ReadFromJsonAsync<ActorDto>())!;
        Assert.Equal(2, person.DepartmentMemberships!.Length);
        Assert.Equal(department.Id, person.ParentId);

        var overlap = await server.Client.PostAsJsonAsync(server.Team("actors"),
            new ActorRequest("Борис", "Person", DepartmentMemberships: [first, new DepartmentMembershipRequest(null, department.Id, new DateOnly(2026, 6, 1), null)]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, overlap.StatusCode);

        var legacyResponse = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Вера", "Person", DepartmentIds: [department.Id]));
        var legacy = (await legacyResponse.Content.ReadFromJsonAsync<ActorDto>())!;
        var membership = Assert.Single(legacy.DepartmentMemberships!);
        Assert.Equal(new DateOnly(1, 1, 1), membership.From);
        Assert.Null(membership.To);
    }
}
