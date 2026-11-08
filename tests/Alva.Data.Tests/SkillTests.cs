using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace Alva.Data.Tests;

public sealed class SkillTests
{
    [Theory]
    [InlineData(BalanceModes.Gentle)]
    [InlineData(BalanceModes.Deadlines)]
    [InlineData(BalanceModes.Maximum)]
    public void Balance_excludes_unskilled_candidates_but_preserves_manual_assignment(string mode)
    {
        var team = Guid.NewGuid();
        var backend = Guid.NewGuid();
        var css = Guid.NewGuid();
        BalancePersonInput[] people = [new(Guid.NewGuid(), "Manual", [team], [backend]), new(Guid.NewGuid(), "Wrong", [team], [css]), new(Guid.NewGuid(), "Match", [team], [backend])];
        var start = new DateOnly(2026, 10, 5);
        var task = new BalanceTaskInput(Guid.NewGuid(), "Task", "Planned", null, 8, start, start, null, team, people[0].Id, [backend]);
        var problem = BalanceMath.Build(mode, Guid.NewGuid(), start, start.AddDays(4), people, CapacityMath.DefaultCalendar, [task], []);
        var options = Assert.Single(problem.Tasks).Options;
        Assert.Equal(0, options[0].Person);
        Assert.DoesNotContain(options, x => x.Person == 1);
        Assert.Contains(options, x => x.Person == 2);
        Assert.Contains(options, x => x.Person == 0);
    }

    [Fact]
    public void Maximum_finds_a_person_with_a_shared_skill_in_another_sub_team()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000001");
        var second = new Guid("00000000-0000-0000-0000-000000000002");
        var shared = Guid.NewGuid();
        BalancePersonInput[] people = [new(Guid.NewGuid(), "A", [first], [shared]), new(Guid.NewGuid(), "B", [second], [shared])];
        var start = new DateOnly(2026, 10, 5);
        var task = new BalanceTaskInput(Guid.NewGuid(), "Task", "Planned", null, 8, start, start, null, people[0].Id, people[0].Id, [shared]);

        var gentle = BalanceMath.Build(BalanceModes.Gentle, Guid.NewGuid(), start, start.AddDays(4), people, CapacityMath.DefaultCalendar, [task], []);
        var maximum = BalanceMath.Build(BalanceModes.Maximum, Guid.NewGuid(), start, start.AddDays(4), people, CapacityMath.DefaultCalendar, [task], []);

        Assert.DoesNotContain(gentle.Tasks.Single().Options, option => option.Person == 1);
        Assert.Contains(maximum.Tasks.Single().Options, option => option.Person == 1);
    }

    [Fact]
    public void A_group_target_admits_people_from_nested_departments()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var start = new DateOnly(2026, 10, 5);
        var person = new BalancePersonInput(Guid.NewGuid(), "Вложенный", [child], [], [child, root]);
        var task = new BalanceTaskInput(Guid.NewGuid(), "Task", "Planned", null, 8, start, start, null, root, null, []);

        var problem = BalanceMath.Build(BalanceModes.Gentle, Guid.NewGuid(), start, start.AddDays(4), [person], CapacityMath.DefaultCalendar, [task], []);

        Assert.Contains(problem.Tasks.Single().Options, option => option.Person == 0);
    }

    [Fact]
    public void Hierarchy_reads_the_parent_that_was_active_on_the_day()
    {
        var department = Guid.NewGuid();
        var oldParent = Guid.NewGuid();
        var newParent = Guid.NewGuid();
        var today = new DateOnly(2027, 1, 5);
        DepartmentParentPeriodDto[] periods =
        [
            new(Guid.NewGuid(), oldParent, new DateOnly(1, 1, 1), today.AddDays(-1)),
            new(Guid.NewGuid(), newParent, today, null)
        ];

        Assert.Equal(oldParent, DepartmentHierarchy.ParentOf(periods, department, today.AddDays(-1), null));
        Assert.Equal(newParent, DepartmentHierarchy.ParentOf(periods, department, today, null));
        Assert.Null(DepartmentHierarchy.ParentOf([], department, today, null));
    }

    [Fact]
    public void Matching_ignores_case_whitespace_and_unicode_width()
    {
        Assert.Equal(["C#", "SQL Server"], SkillMath.Normalize([" C# ", "c#", " SQL  Server ", ""]));
        Assert.Empty(SkillMath.Missing(["Ｃ＃", "SQL Server"], ["c#", "sql\tserver"]));
        Assert.Equal(["Testing"], SkillMath.Missing(["C#", "Testing"], ["c#"]));
        Assert.True(SkillMath.Compatible(Array.Empty<string>(), Array.Empty<string>()));
        Assert.False(SkillMath.Compatible(["C#"], []));
    }

    [Fact]
    public void Cyrillic_skill_tags_are_preserved_and_compared_case_insensitively()
    {
        Assert.Equal(["Платформа"], SkillMath.Normalize([" Платформа "]));
        Assert.Empty(SkillMath.Missing(["Платформа"], [" платформа "]));
        Assert.False(SkillMath.Compatible(["Платформа"], ["Аналитика"]));
    }

    [Fact]
    public void Skill_identifiers_are_compared_without_duplicates()
    {
        var backend = Guid.NewGuid();
        var sql = Guid.NewGuid();

        Assert.Equal([backend, sql], SkillMath.Normalize([backend, sql, backend]));
        Assert.Empty(SkillMath.Missing([backend, sql], [sql, backend]));
        Assert.Equal([sql], SkillMath.Missing([backend, sql], [backend]));
        Assert.True(SkillMath.Compatible(Array.Empty<Guid>(), Array.Empty<Guid>()));
    }

    [Fact]
    public void Tags_are_bounded_and_legacy_requests_do_not_clear_skills()
    {
        Assert.Throws<ArgumentException>(() => SkillMath.Normalize([new string('a', 81)]));
        Assert.Throws<ArgumentException>(() => SkillMath.Normalize(Enumerable.Range(0, 51).Select(i => i.ToString())));
        var request = JsonSerializer.Deserialize<TaskRequest>("{\"title\":\"Old client\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(request.RequiredSkillIds);
    }

    [Fact]
    public async Task Skills_survive_database_reload()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AlvaDbContext(new DbContextOptionsBuilder<AlvaDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var workspace = new Workspace { Name = "Workspace" };
        var team = new Team { WorkspaceId = workspace.Id, Name = "Team" };
        var backend = new Actor { TeamId = team.Id, Name = "Backend", Kind = ActorKind.Skill };
        var sql = new Actor { TeamId = team.Id, Name = "SQL", Kind = ActorKind.Skill };
        var person = new Actor { TeamId = team.Id, Name = "Person", Kind = ActorKind.Person };
        var task = new TaskCard { TeamId = team.Id, Title = "Task" };
        db.AddRange(workspace, team, backend, sql, person, task);
        db.Add(new ActorSkill { ActorId = person.Id, SkillId = backend.Id });
        db.Add(new TaskSkill { TaskId = task.Id, SkillId = sql.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(backend.Id, (await db.ActorSkills.SingleAsync()).SkillId);
        Assert.Equal(sql.Id, (await db.TaskSkills.SingleAsync()).SkillId);
    }
}
