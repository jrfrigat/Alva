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
        var role = Guid.NewGuid();
        BalancePersonInput[] people = [new(Guid.NewGuid(), "Manual", [role], [], []), new(Guid.NewGuid(), "Wrong", [role], [], ["CSS"]), new(Guid.NewGuid(), "Match", [role], [], ["SQL"])];
        var start = new DateOnly(2026, 10, 5);
        var task = new BalanceTaskInput(Guid.NewGuid(), "Task", "Planned", null, 8, start, start, null, role, people[0].Id, ["sql"]);
        var problem = BalanceMath.Build(mode, Guid.NewGuid(), start, start.AddDays(4), people, CapacityMath.DefaultCalendar, [task], []);
        var options = Assert.Single(problem.Tasks).Options;
        Assert.Equal(0, options[0].Person);
        Assert.DoesNotContain(options, x => x.Person == 1);
        Assert.Contains(options, x => x.Person == 2);
        Assert.Single(options, x => x.Person == 0);
    }

    [Fact]
    public void Matching_ignores_case_whitespace_and_unicode_width()
    {
        Assert.Equal(["C#", "SQL Server"], SkillMath.Normalize([" C# ", "c#", " SQL  Server ", ""]));
        Assert.Empty(SkillMath.Missing(["Ｃ＃", "SQL Server"], ["c#", "sql\tserver"]));
        Assert.Equal(["Testing"], SkillMath.Missing(["C#", "Testing"], ["c#"]));
        Assert.True(SkillMath.Compatible([], []));
        Assert.False(SkillMath.Compatible(["C#"], []));
    }

    [Fact]
    public void Tags_are_bounded_and_legacy_requests_do_not_clear_skills()
    {
        Assert.Throws<ArgumentException>(() => SkillMath.Normalize([new string('a', 81)]));
        Assert.Throws<ArgumentException>(() => SkillMath.Normalize(Enumerable.Range(0, 51).Select(i => i.ToString())));
        var request = JsonSerializer.Deserialize<TaskRequest>("{\"title\":\"Old client\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(request.RequiredSkills);
        var actor = JsonSerializer.Deserialize<Actor>("{}")!;
        Assert.Empty(actor.Skills);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public void Transfers_preserve_skills_and_reject_invalid_tags(string format)
    {
        var team = Guid.NewGuid();
        var actor = new Actor { TeamId = team, Name = "Person", Skills = ["C#", "Анализ"] };
        var task = new TaskCard { TeamId = team, Title = "Task", RequiredSkills = ["C#", "SQL"] };
        DataRow Row(string kind, Entity entity) => new(kind, entity.Id, JsonSerializer.SerializeToElement(entity, entity.GetType(), DataTransfer.Json));
        var package = new DataPackage(1, team, [Row("actor", actor), Row("task", task)]);
        var result = DataTransfer.Decode(DataTransfer.Encode(package, format), format);
        Assert.Empty(DataTransfer.Validate(result));
        Assert.Equal(actor.Skills, ((Actor)DataTransfer.Entity(result.Rows[0])).Skills);
        Assert.Equal(task.RequiredSkills, ((TaskCard)DataTransfer.Entity(result.Rows[1])).RequiredSkills);
        task.RequiredSkills = [new string('a', 81)];
        Assert.Contains(DataTransfer.Validate(new(1, team, [Row("task", task)])), x => x.Field == "skills");
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
        var actor = new Actor { TeamId = team.Id, Name = "Person", Skills = ["C#"] };
        var task = new TaskCard { TeamId = team.Id, Title = "Task", RequiredSkills = ["SQL"] };
        db.AddRange(workspace, team, actor, task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(["C#"], (await db.Actors.SingleAsync()).Skills);
        Assert.Equal(["SQL"], (await db.Tasks.SingleAsync()).RequiredSkills);
    }
}
