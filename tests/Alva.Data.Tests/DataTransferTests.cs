using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Alva.Data.Tests;

public sealed class DataTransferTests
{
    private static readonly Guid TeamId = Guid.NewGuid();
    private static DataRow Row(string kind, Entity value) => new(kind, value.Id, JsonSerializer.SerializeToElement(value, value.GetType(), DataTransfer.Json));
    private static DataPackage Sample()
    {
        var project = new Project { WorkspaceId = Guid.NewGuid(), Name = "Проект, \"Alva\"", Description = "Первая строка\nВторая строка" };
        var sprint = new Sprint { TeamId = TeamId, Name = "Спринт", StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 14) };
        var parent = new TaskCard { TeamId = TeamId, Title = "Parent", ProjectId = project.Id };
        var task = new TaskCard { TeamId = TeamId, Title = "Child", ParentId = parent.Id, ProjectId = project.Id, StartDate = new(2026, 10, 2), EndDate = new(2026, 10, 6), EstimateHours = 3.25m };
        var actor = new Actor { TeamId = TeamId, Name = "Мария", Kind = ActorKind.Person };
        var calendar = new WorkCalendar { ActorId = actor.Id };
        return new(1, TeamId, [Row("project", project), Row("sprint", sprint), Row("task", parent), Row("task", task), Row("actor", actor), Row("calendar", calendar), Row("assignment", new Assignment { TaskId = task.Id, TargetId = actor.Id }), Row("exception", new CalendarException { CalendarId = calendar.Id, From = new(2026, 10, 2), To = new(2026, 10, 2), Reason = "Отпуск" }), Row("block", new RecurringBlock { CalendarId = calendar.Id, DayOfWeek = DayOfWeek.Monday, Hours = 1, Name = "Встреча" })]);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public void Every_format_preserves_graph_unicode_quotes_and_decimal(string format)
    {
        var source = Sample();
        var result = DataTransfer.Decode(DataTransfer.Encode(source, format), format);
        Assert.Empty(DataTransfer.Validate(result));
        Assert.Equal(source.SourceTeamId, result.SourceTeamId);
        Assert.Equal(source.Rows.Select(x => (x.Kind, x.Id, x.Payload.GetRawText())), result.Rows.Select(x => (x.Kind, x.Id, x.Payload.GetRawText())));
    }

    [Fact]
    public void Rejects_cross_team_tasks_and_dangling_references()
    {
        var task = new TaskCard { TeamId = Guid.NewGuid(), Title = "Task", ProjectId = Guid.NewGuid() };
        var errors = DataTransfer.Validate(new(1, TeamId, [Row("task", task)]));
        Assert.Contains(errors, x => x.Row == 1 && x.Field == "teamId");
        Assert.Contains(errors, x => x.Field == "projectId");
    }

    [Fact]
    public void Rejects_parent_and_dependency_cycles_without_recursion()
    {
        var a = new TaskCard { TeamId = TeamId, Title = "A" }; var b = new TaskCard { TeamId = TeamId, Title = "B", ParentId = a.Id }; a.ParentId = b.Id;
        var package = new DataPackage(1, TeamId, [Row("task", a), Row("task", b), Row("dependency", new TaskDependency { PredecessorId = a.Id, SuccessorId = b.Id }), Row("dependency", new TaskDependency { PredecessorId = b.Id, SuccessorId = a.Id })]);
        var errors = DataTransfer.Validate(package);
        Assert.Contains(errors, x => x.Field == "task.parent"); Assert.Contains(errors, x => x.Field == "dependency");
    }

    [Fact]
    public void Rejects_user_bindings_duplicate_ids_and_unsupported_entity_types()
    {
        var actor = new Actor { TeamId = TeamId, Name = "A", UserId = Guid.NewGuid() }; var row = Row("actor", actor);
        var errors = DataTransfer.Validate(new(1, TeamId, [row, row, new("user", Guid.NewGuid(), row.Payload)]));
        Assert.Contains(errors, x => x.Field == "userId"); Assert.Contains(errors, x => x.Row == 2 && x.Field == "id"); Assert.Contains(errors, x => x.Row == 3 && x.Field == "payload");
    }

    [Fact]
    public void Rejects_invalid_calendar_hours_dates_and_enums()
    {
        var actor = new Actor { TeamId = TeamId, Name = "A", Kind = (ActorKind)100 };
        var calendar = new WorkCalendar { ActorId = actor.Id, Monday = 25, TimeZone = "not/a/timezone" };
        var exception = new CalendarException { CalendarId = calendar.Id, From = new(2026, 10, 3), To = new(2026, 10, 2), AvailableHours = -1 };
        var errors = DataTransfer.Validate(new(1, TeamId, [Row("actor", actor), Row("calendar", calendar), Row("exception", exception)]));
        Assert.Contains(errors, x => x.Field == "Kind"); Assert.Contains(errors, x => x.Field == "timeZone"); Assert.Contains(errors, x => x.Field == "hours"); Assert.Contains(errors, x => x.Field == "dates");
    }

    [Fact]
    public void Rejects_assignment_to_parent_and_unrelated_person_resolution()
    {
        var parent = new TaskCard { TeamId = TeamId, Title = "Parent" }; var child = new TaskCard { TeamId = TeamId, Title = "Child", ParentId = parent.Id };
        var person = new Actor { TeamId = TeamId, Kind = ActorKind.Person, Name = "Person" }; var role = new Actor { TeamId = TeamId, Kind = ActorKind.Role, Name = "Role" };
        var assignment = new Assignment { TaskId = parent.Id, TargetId = role.Id, ResolvedPersonId = person.Id };
        var errors = DataTransfer.Validate(new(1, TeamId, [Row("task", parent), Row("task", child), Row("actor", person), Row("actor", role), Row("assignment", assignment)]));
        Assert.Contains(errors, x => x.Field == "taskId"); Assert.Contains(errors, x => x.Field == "resolvedPersonId");
    }

    [Fact]
    public void Rejects_unknown_payload_fields_and_missing_explicit_identity()
    {
        var row = new DataRow("actor", Guid.NewGuid(), JsonDocument.Parse("{\"name\":\"A\",\"isAdmin\":true}").RootElement.Clone());
        Assert.Contains(DataTransfer.Validate(new(1, TeamId, [row])), x => x.Field == "payload");
        row = new("actor", Guid.NewGuid(), JsonDocument.Parse($"{{\"name\":\"A\",\"teamId\":\"{TeamId}\"}}").RootElement.Clone());
        Assert.Contains(DataTransfer.Validate(new(1, TeamId, [row])), x => x.Field == "payload.id");
    }

    [Fact]
    public void Rejects_malformed_csv_and_oversized_upload()
    {
        Assert.Throws<FormatException>(() => DataTransfer.Decode(Encoding.UTF8.GetBytes("\"unterminated"), "csv"));
        Assert.Throws<FormatException>(() => DataTransfer.Decode(new byte[DataTransfer.MaxBytes + 1], "json"));
    }

    [Theory]
    [InlineData("<f>1+1</f>", "")]
    [InlineData("", "<Relationship TargetMode=\"External\" Target=\"https://evil.example\"/> ")]
    public void Rejects_xlsx_formulas_and_external_links(string formula, string relationship)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open())) w.Write($"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row><c r=\"A1\" t=\"inlineStr\">{formula}<is><t>kind</t></is></c></row></sheetData></worksheet>");
            using (var w = new StreamWriter(zip.CreateEntry("xl/_rels/workbook.xml.rels").Open())) w.Write($"<Relationships>{relationship}</Relationships>");
        }
        Assert.Throws<FormatException>(() => DataTransfer.Decode(buffer.ToArray(), "xlsx"));
    }
}
