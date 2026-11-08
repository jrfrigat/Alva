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

    [Fact]
    public async Task Moving_a_department_records_parent_history_and_updates_same_day()
    {
        var server = await ApiServer.Create(app => app.MapActors());
        await using var _ = server;
        async Task<ActorDto> Actor(ActorRequest request)
        {
            var response = await server.Client.PostAsJsonAsync(server.Team("actors"), request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ActorDto>())!;
        }
        var first = await Actor(new ActorRequest("Разработка", "Department"));
        var second = await Actor(new ActorRequest("Платформа", "Department"));
        var third = await Actor(new ActorRequest("Продукт", "Department"));

        var moved = await server.Client.PutAsJsonAsync(server.Team($"actors/{second.Id}"), new ActorRequest("Платформа", "Department", ParentId: first.Id));
        Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        var afterFirstMove = (await moved.Content.ReadFromJsonAsync<ActorDto>())!;
        Assert.Equal(first.Id, afterFirstMove.ParentId);
        Assert.Equal(2, afterFirstMove.DepartmentParentPeriods!.Length);

        var cycle = await server.Client.PutAsJsonAsync(server.Team($"actors/{first.Id}"), new ActorRequest("Разработка", "Department", ParentId: second.Id));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, cycle.StatusCode);

        var again = await server.Client.PutAsJsonAsync(server.Team($"actors/{second.Id}"), new ActorRequest("Платформа", "Department", ParentId: third.Id));
        var afterSecondMove = (await again.Content.ReadFromJsonAsync<ActorDto>())!;
        Assert.Equal(third.Id, afterSecondMove.ParentId);
        Assert.Equal(2, afterSecondMove.DepartmentParentPeriods!.Length);
        Assert.Equal(third.Id, afterSecondMove.DepartmentParentPeriods.Single(p => p.To is null).ParentId);
    }

    [Fact]
    public async Task A_task_targeted_at_a_parent_department_accepts_people_from_its_children()
    {
        var server = await ApiServer.Create(app => { app.MapActors(); app.MapTasks(); });
        await using var _ = server;
        async Task<ActorDto> Actor(ActorRequest request)
        {
            var response = await server.Client.PostAsJsonAsync(server.Team("actors"), request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ActorDto>())!;
        }
        var root = await Actor(new ActorRequest("Разработка", "Department"));
        var child = await Actor(new ActorRequest("Платформа", "Department", ParentId: root.Id));
        var other = await Actor(new ActorRequest("Продажи", "Department"));
        var member = await Actor(new ActorRequest("Анна", "Person", DepartmentMemberships:
            [new DepartmentMembershipRequest(null, child.Id, new DateOnly(2026, 1, 1), null)]));
        var outsider = await Actor(new ActorRequest("Борис", "Person", DepartmentMemberships:
            [new DepartmentMembershipRequest(null, other.Id, new DateOnly(2026, 1, 1), null)]));
        var taskResponse = await server.Client.PostAsJsonAsync(server.Team("tasks"), new TaskRequest("Задача", StartDate: new DateOnly(2026, 2, 1), EndDate: new DateOnly(2026, 2, 2)));
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDto>())!;

        var accepted = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}/assignment"), new AssignmentRequest(root.Id, member.Id));
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());

        var rejected = await server.Client.PutAsJsonAsync(server.Team($"tasks/{task.Id}/assignment"), new AssignmentRequest(root.Id, outsider.Id));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode);
    }
}
