using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace Alva.Api;

public static class DataEndpoints
{
    public static void MapData(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}/data").RequireAuthorization();
        group.MapGet("/export", async (Guid teamId, string? format, AlvaDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "view");
            await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var rows = new List<DataRow>();
            void Add<T>(string kind, IEnumerable<T> values) where T : Entity => rows.AddRange(values.Select(x => new DataRow(kind, x.Id, JsonSerializer.SerializeToElement(x, DataTransfer.Json))));
            var projectIds = await db.ProjectTeams.Where(x => x.TeamId == teamId).Select(x => x.ProjectId).ToArrayAsync(ct);
            Add("project", await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.Id)).ToArrayAsync(ct));
            Add("sprint", await db.Sprints.AsNoTracking().Where(x => x.TeamId == teamId).ToArrayAsync(ct));
            var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId).ToArrayAsync(ct);
            var actors = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId).ToArrayAsync(ct);
            // User accounts and memberships are never portable across workspaces.
            foreach (var actor in actors) actor.UserId = null;
            Add("task", tasks); Add("actor", actors);
            var taskIds = tasks.Select(x => x.Id).ToArray(); var actorIds = actors.Select(x => x.Id).ToArray();
            Add("assignment", await db.Assignments.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToArrayAsync(ct));
            Add("dependency", await db.Dependencies.AsNoTracking().Where(x => taskIds.Contains(x.PredecessorId) || taskIds.Contains(x.SuccessorId)).ToArrayAsync(ct));
            foreach (var role in await db.ActorRoles.AsNoTracking().Where(x => actorIds.Contains(x.ActorId) || actorIds.Contains(x.RoleId)).ToArrayAsync(ct))
                rows.Add(new("actorRole", Guid.NewGuid(), JsonSerializer.SerializeToElement(role, DataTransfer.Json)));
            foreach (var department in await db.ActorDepartments.AsNoTracking().Where(x => actorIds.Contains(x.ActorId) || actorIds.Contains(x.DepartmentId)).ToArrayAsync(ct))
                rows.Add(new("actorDepartment", Guid.NewGuid(), JsonSerializer.SerializeToElement(department, DataTransfer.Json)));
            var calendars = await db.Calendars.AsNoTracking().Where(x => actorIds.Contains(x.ActorId)).ToArrayAsync(ct);
            Add("calendar", calendars);
            var calendarIds = calendars.Select(x => x.Id).ToArray();
            Add("exception", await db.CalendarExceptions.AsNoTracking().Where(x => calendarIds.Contains(x.CalendarId)).ToArrayAsync(ct));
            Add("block", await db.RecurringBlocks.AsNoTracking().Where(x => calendarIds.Contains(x.CalendarId)).ToArrayAsync(ct));
            var package = new DataPackage(1, teamId, rows.ToArray());
            var errors = DataTransfer.Validate(package);
            if (errors.Count != 0) return Results.Conflict(new { message = "Данные команды содержат непереносимые связи. Исправьте их перед экспортом.", errors });
            await snapshot.CommitAsync(ct);
            var selected = DataTransfer.Format(format);
            byte[] encoded;
            try { encoded = DataTransfer.Encode(package, selected); }
            catch (FormatException ex) { return Results.BadRequest(new { message = ex.Message }); }
            if (encoded.Length > DataTransfer.MaxBytes) return Results.Problem(statusCode: 413, title: "Экспорт превышает переносимый лимит 10 МБ. Разделите данные команды.");
            return Results.File(encoded, selected switch { "csv" => "text/csv; charset=utf-8", "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", _ => "application/json" }, $"alva-{teamId}.{selected}");
        });
        group.MapPost("/import", async (Guid teamId, string? format, HttpRequest request, AlvaDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "manage");
            if (request.ContentLength > DataTransfer.MaxBytes) return Results.Problem(statusCode: 413, title: "Файл превышает 10 МБ.");
            using var body = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (body.Length + read > DataTransfer.MaxBytes) return Results.Problem(statusCode: 413, title: "Файл превышает 10 МБ.");
                body.Write(buffer, 0, read);
            }
            DataPackage package;
            try { package = DataTransfer.Decode(body.ToArray(), DataTransfer.Format(format)); }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException or XmlException or ArgumentException)
            { return Results.BadRequest(new { errors = new[] { new DataIssue(0, "file", "Некорректный формат: " + ex.Message) } }); }
            var errors = DataTransfer.Validate(package);
            if (errors.Count > 0) return Results.BadRequest(new { errors });
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Recheck membership under the transaction before any write.
            await AccessControl.RequireTeam(db, user, teamId, "data", "manage");
            var team = await db.Teams.SingleAsync(x => x.Id == teamId, ct);
            var idMap = package.Rows.ToDictionary(x => x.Id, _ => Guid.NewGuid());
            // Старые выгрузки: задача без дат из спринта получает окно этого спринта — спринт теперь следует из дат.
            var sprintWindows = package.Rows.Where(x => x.Kind == "sprint").Select(x => (Sprint)DataTransfer.Entity(x)).ToDictionary(x => x.Id);
            Guid Map(Guid id) => idMap[id]; Guid? Optional(Guid? id) => id.HasValue ? Map(id.Value) : null;
            foreach (var row in package.Rows)
            {
                var value = DataTransfer.Entity(row);
                if (value is Entity entity) entity.Id = Map(row.Id);
                switch (value)
                {
                    case Project x: x.WorkspaceId = team.WorkspaceId; db.Projects.Add(x); db.ProjectTeams.Add(new() { ProjectId = x.Id, TeamId = teamId }); break;
                    case Sprint x: x.TeamId = teamId; db.Sprints.Add(x); break;
                    case TaskCard x:
                        if (DataTransfer.LegacySprint(row) is { } legacySprint && sprintWindows.GetValueOrDefault(legacySprint) is { } window)
                        {
                            x.StartDate ??= x.EndDate is { } end && end < window.StartDate ? end : window.StartDate;
                            x.EndDate ??= x.StartDate > window.EndDate ? x.StartDate : window.EndDate;
                        }
                        x.TeamId = teamId; x.ProjectId = Optional(x.ProjectId); x.ParentId = Optional(x.ParentId); x.Version = 1; db.Tasks.Add(x); break;
                    case Actor x: x.TeamId = teamId; x.ParentId = Optional(x.ParentId); x.UserId = null; db.Actors.Add(x); break;
                    case ActorRole x: x.ActorId = Map(x.ActorId); x.RoleId = Map(x.RoleId); db.ActorRoles.Add(x); break;
                    case ActorDepartment x: x.ActorId = Map(x.ActorId); x.DepartmentId = Map(x.DepartmentId); db.ActorDepartments.Add(x); break;
                    case Assignment x: x.TaskId = Map(x.TaskId); x.TargetId = Map(x.TargetId); x.ResolvedPersonId = Optional(x.ResolvedPersonId); db.Assignments.Add(x); break;
                    case TaskDependency x: x.PredecessorId = Map(x.PredecessorId); x.SuccessorId = Map(x.SuccessorId); db.Dependencies.Add(x); break;
                    case WorkCalendar x: x.ActorId = Map(x.ActorId); db.Calendars.Add(x); break;
                    case CalendarException x: x.CalendarId = Map(x.CalendarId); db.CalendarExceptions.Add(x); break;
                    case RecurringBlock x: x.CalendarId = Map(x.CalendarId); db.RecurringBlocks.Add(x); break;
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new DataImportResult(package.Rows.Length, idMap, ["Импорт создаёт новые записи. Связи пользователей, права и история не переносятся; привяжите участников к пользователям отдельно."]));
        });
    }
}

/// <summary>Bounded, versioned, lossless interchange. No workbook formulas are evaluated.</summary>
public static class DataTransfer
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxRows = 10000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Format(string? value) => value?.ToLowerInvariant() switch { null or "json" => "json", "csv" => "csv", "xlsx" => "xlsx", _ => throw new ApiException(400, "Поддерживаются JSON, CSV и XLSX.") };
    public static object Entity(DataRow row) => row.Kind switch
    {
        "project" => Read<Project>(row), "sprint" => Read<Sprint>(row), "task" => ReadTask(row), "actor" => Read<Actor>(row),
        "actorRole" => Read<ActorRole>(row), "actorDepartment" => Read<ActorDepartment>(row), "assignment" => Read<Assignment>(row), "dependency" => Read<TaskDependency>(row),
        "calendar" => Read<WorkCalendar>(row), "exception" => Read<CalendarException>(row), "block" => Read<RecurringBlock>(row),
        _ => throw new FormatException("Неизвестный тип записи.")
    };
    private static T Read<T>(DataRow row) => row.Payload.Deserialize<T>(Json) ?? throw new JsonException("Пустая запись.");

    /// <summary>Задача без устаревшего поля sprintId: спринт теперь следует из дат (см. LegacySprint).</summary>
    private static TaskCard ReadTask(DataRow row)
    {
        if (!row.Payload.TryGetProperty("sprintId", out _)) return Read<TaskCard>(row);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(row.Payload.GetRawText())!.AsObject();
        payload.Remove("sprintId");
        return payload.Deserialize<TaskCard>(Json) ?? throw new JsonException("Пустая запись.");
    }

    /// <summary>Спринт задачи из выгрузок до STORY-27; задача без дат получает при импорте его окно.</summary>
    public static Guid? LegacySprint(DataRow row) =>
        row.Kind == "task" && row.Payload.TryGetProperty("sprintId", out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) ? id : null;

    public static List<DataIssue> Validate(DataPackage? package)
    {
        var errors = new List<DataIssue>();
        if (package is null || package.Version != 1 || package.SourceTeamId == Guid.Empty || package.Rows is null || package.Rows.Length > MaxRows)
        { errors.Add(new(0, "package", "Нужны version=1, sourceTeamId и не более 10000 записей.")); return errors; }
        var values = new Dictionary<Guid, object>(); var rowNumbers = new Dictionary<Guid, int>();
        var legacy = package.Rows.Where(r => r is not null).GroupBy(r => r.Id).Select(g => g.First()).Where(r => LegacySprint(r) is not null).ToDictionary(r => r.Id, LegacySprint);
        for (var i = 0; i < package.Rows.Length; i++)
        {
            var row = package.Rows[i];
            if (row is null || row.Id == Guid.Empty || values.ContainsKey(row.Id)) { errors.Add(new(i + 1, "id", "Пустой или повторяющийся идентификатор.")); continue; }
            try
            {
                var value = Entity(row); values.Add(row.Id, value); rowNumbers.Add(row.Id, i + 1);
                if (value is Entity entity && entity.Id != row.Id) errors.Add(new(i + 1, "id", "Идентификатор payload не совпадает с id строки."));
                // Missing IDs must never be silently filled by entity constructors.
                if (value is Entity && (!row.Payload.TryGetProperty("id", out var id) || !id.TryGetGuid(out var parsed) || parsed != row.Id)) errors.Add(new(i + 1, "payload.id", "Явный идентификатор обязателен."));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { errors.Add(new(i + 1, "payload", "Некорректная запись: " + ex.Message)); }
        }
        void Error(Guid id, string field, string text) => errors.Add(new(rowNumbers[id], field, text));
        bool Ref<T>(Guid owner, Guid? id, string field, bool optional = false)
        { if (id is null && optional) return true; if (id is null || !values.TryGetValue(id.Value, out var found) || found is not T) { Error(owner, field, "Связь должна указывать на запись соответствующего типа внутри файла."); return false; } return true; }
        foreach (var (id, value) in values)
        {
            foreach (var property in value.GetType().GetProperties())
            {
                if (property.PropertyType == typeof(string) && property.GetValue(value) is null && property.Name != "Email") Error(id, property.Name, "Обязательная строка не может быть null.");
                if (property.PropertyType == typeof(string) && property.GetValue(value) is string text && text.Length > (property.Name is "Description" or "Summary" ? 10000 : 500)) Error(id, property.Name, "Строка превышает допустимую длину.");
                if (property.PropertyType.IsEnum && !Enum.IsDefined(property.PropertyType, property.GetValue(value)!)) Error(id, property.Name, "Неизвестное значение перечисления.");
                if (property.PropertyType == typeof(decimal) || property.PropertyType == typeof(decimal?))
                    if (property.GetValue(value) is decimal number && (number < 0 || number > 9999999999.99m || decimal.Round(number, 2) != number)) Error(id, property.Name, "Число должно быть неотрицательным и иметь не более двух знаков после запятой.");
            }
            switch (value)
            {
                case Project x: Name(id, x.Name); if (x.WorkspaceId == Guid.Empty) Error(id, "workspaceId", "Пространство источника обязательно."); break;
                case Sprint x: Team(id, x.TeamId); Name(id, x.Name); Dates(id, x.StartDate, x.EndDate); break;
                case TaskCard x:
                    Team(id, x.TeamId); Name(id, x.Title); Ref<Project>(id, x.ProjectId, "projectId", true); Ref<Sprint>(id, legacy.GetValueOrDefault(id), "sprintId", true); Ref<TaskCard>(id, x.ParentId, "parentId", true);
                    if (x.ParentId == id) Error(id, "parentId", "Задача не может быть собственным родителем.");
                    if (x.StartDate.HasValue && x.EndDate.HasValue) Dates(id, x.StartDate.Value, x.EndDate.Value);
                    if (x.Version < 1 || x.Order < 0) Error(id, "version/order", "Версия должна быть положительной, порядок — неотрицательным."); break;
                case Actor x:
                    Team(id, x.TeamId); Name(id, x.Name); Ref<Actor>(id, x.ParentId, "parentId", true);
                    if (x.UserId.HasValue) Error(id, "userId", "Привязки пользователей нельзя импортировать.");
                    if (x.ParentId is { } parent && values.TryGetValue(parent, out var p) && p is Actor pa && (pa.Kind != ActorKind.Department || x.Kind == ActorKind.Role)) Error(id, "parentId", "Родителем участника может быть только подразделение; роль не имеет родителя."); break;
                case ActorRole x:
                    Ref<Actor>(id, x.ActorId, "actorId"); Ref<Actor>(id, x.RoleId, "roleId");
                    if (values.GetValueOrDefault(x.ActorId) is Actor a && a.Kind != ActorKind.Person || values.GetValueOrDefault(x.RoleId) is Actor role && role.Kind != ActorKind.Role) Error(id, "role", "Роль связывается только с человеком."); break;
                case ActorDepartment x:
                    Ref<Actor>(id, x.ActorId, "actorId"); Ref<Actor>(id, x.DepartmentId, "departmentId");
                    if (values.GetValueOrDefault(x.ActorId) is Actor personActor && personActor.Kind != ActorKind.Person || values.GetValueOrDefault(x.DepartmentId) is Actor department && department.Kind != ActorKind.Department) Error(id, "department", "Подразделение связывается только с человеком."); break;
                case Assignment x:
                    Ref<TaskCard>(id, x.TaskId, "taskId"); Ref<Actor>(id, x.TargetId, "targetId"); Ref<Actor>(id, x.ResolvedPersonId, "resolvedPersonId", true);
                    if (values.Values.OfType<TaskCard>().Any(t => t.ParentId == x.TaskId)) Error(id, "taskId", "Назначение допустимо только для листовой задачи.");
                    if (x.ResolvedPersonId is { } person)
                    {
                        if (values.GetValueOrDefault(person) is not Actor { Kind: ActorKind.Person }) Error(id, "resolvedPersonId", "Исполнитель должен быть человеком.");
                        if (values.GetValueOrDefault(x.TargetId) is Actor target && !(target.Id == person || target.Kind == ActorKind.Role && values.Values.OfType<ActorRole>().Any(r => r.ActorId == person && r.RoleId == target.Id) || target.Kind == ActorKind.Department && Descendant(person, target.Id))) Error(id, "resolvedPersonId", "Исполнитель не принадлежит назначенной роли/подразделению.");
                    }
                    break;
                case TaskDependency x: Ref<TaskCard>(id, x.PredecessorId, "predecessorId"); Ref<TaskCard>(id, x.SuccessorId, "successorId"); if (x.PredecessorId == x.SuccessorId) Error(id, "dependency", "Задача не может зависеть от себя."); if (x.LagDays is < -3650 or > 3650) Error(id, "lagDays", "Сдвиг ограничен 3650 днями в обе стороны."); break;
                case WorkCalendar x:
                    Ref<Actor>(id, x.ActorId, "actorId");
                    try { TimeZoneInfo.FindSystemTimeZoneById(x.TimeZone); } catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentNullException) { Error(id, "timeZone", "Неизвестный часовой пояс."); }
                    if (new[] { x.Monday, x.Tuesday, x.Wednesday, x.Thursday, x.Friday, x.Saturday, x.Sunday }.Any(h => h > 24)) Error(id, "hours", "Не более 24 часов в сутки."); break;
                case CalendarException x: Ref<WorkCalendar>(id, x.CalendarId, "calendarId"); Dates(id, x.From, x.To); if (x.AvailableHours > 24) Error(id, "hours", "Не более 24 часов в сутки."); break;
                case RecurringBlock x: Ref<WorkCalendar>(id, x.CalendarId, "calendarId"); Name(id, x.Name); if (x.Hours > 24) Error(id, "hours", "Не более 24 часов в сутки."); break;
            }
        }
        void Name(Guid id, string? name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 200) Error(id, "name", "Название должно содержать 1–200 символов."); }
        void Team(Guid id, Guid team) { if (team != package.SourceTeamId) Error(id, "teamId", "Запись принадлежит другой команде."); }
        void Dates(Guid id, DateOnly start, DateOnly end) { if (end < start) Error(id, "dates", "Конец раньше начала."); }
        bool Descendant(Guid person, Guid department)
        {
            bool Walk(Guid start) { var seen = new HashSet<Guid>(); for (Guid? current = start; current.HasValue && seen.Add(current.Value); current = (values.GetValueOrDefault(current.Value) as Actor)?.ParentId) if (current == department) return true; return false; }
            return Walk(person) || values.Values.OfType<ActorDepartment>().Where(x => x.ActorId == person).Any(x => Walk(x.DepartmentId));
        }
        Duplicate<Assignment>(x => x.TaskId, "assignment"); Duplicate<WorkCalendar>(x => x.ActorId, "calendar"); Duplicate<TaskDependency>(x => (x.PredecessorId, x.SuccessorId), "dependency"); Duplicate<ActorRole>(x => (x.ActorId, x.RoleId), "actorRole"); Duplicate<ActorDepartment>(x => (x.ActorId, x.DepartmentId), "actorDepartment");
        void Duplicate<T>(Func<T, object> key, string field) { foreach (var group in values.Where(x => x.Value is T).GroupBy(x => key((T)x.Value)).Where(x => x.Count() > 1)) foreach (var entry in group) Error(entry.Key, field, "Повторяющаяся связь."); }
        Cycles(values.Where(x => x.Value is TaskCard).Select(x => (x.Key, ((TaskCard)x.Value).ParentId)), "task.parent");
        Cycles(values.Where(x => x.Value is Actor).Select(x => (x.Key, ((Actor)x.Value).ParentId)), "actor.parent");
        var dependencies = values.Where(x => x.Value is TaskDependency).ToArray();
        var adjacency = dependencies.Select(x => (TaskDependency)x.Value).GroupBy(x => x.PredecessorId).ToDictionary(x => x.Key, x => x.Select(y => y.SuccessorId).ToArray());
        if (HasCycle(adjacency)) foreach (var dep in dependencies) Error(dep.Key, "dependency", "Граф зависимостей содержит цикл.");
        void Cycles(IEnumerable<(Guid Id, Guid? Parent)> pairs, string field)
        {
            var graph = pairs.Where(x => x.Parent.HasValue).ToDictionary(x => x.Id, x => new[] { x.Parent!.Value });
            if (HasCycle(graph)) foreach (var id in graph.Keys) Error(id, field, "Иерархия содержит цикл.");
        }
        return errors.Take(100).ToList();
    }
    private static bool HasCycle(Dictionary<Guid, Guid[]> graph)
    {
        // Iterative topological sort avoids stack overflow on malicious deep graphs.
        var nodes = graph.Keys.Concat(graph.Values.SelectMany(x => x)).Distinct().ToDictionary(x => x, _ => 0);
        foreach (var child in graph.Values.SelectMany(x => x)) nodes[child]++;
        var queue = new Queue<Guid>(nodes.Where(x => x.Value == 0).Select(x => x.Key)); var count = 0;
        while (queue.TryDequeue(out var id)) { count++; foreach (var child in graph.GetValueOrDefault(id) ?? []) if (--nodes[child] == 0) queue.Enqueue(child); }
        return count != nodes.Count;
    }

    public static byte[] Encode(DataPackage package, string format)
    {
        if (format == "json") return JsonSerializer.SerializeToUtf8Bytes(package, Json);
        var rows = new List<string[]> { new[] { "kind", "id", "payload" }, new[] { "metadata", package.SourceTeamId.ToString(), package.Version.ToString(CultureInfo.InvariantCulture) } };
        rows.AddRange(package.Rows.Select(x => new[] { x.Kind, x.Id.ToString(), x.Payload.GetRawText() }));
        if (format == "csv") return Encoding.UTF8.GetBytes(string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(v => "\"" + v.Replace("\"", "\"\"") + "\"")))));
        if (format != "xlsx") throw new FormatException("Неизвестный формат.");
        if (rows.SelectMany(x => x).Any(x => x.Length > 32767)) throw new FormatException("Ячейка превышает лимит XLSX 32767 символов. Используйте JSON или CSV.");
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheet = new XDocument(new XElement(s + "worksheet", new XElement(s + "sheetData", rows.Select((r, i) => new XElement(s + "row", new XAttribute("r", i + 1), r.Select((v, j) => new XElement(s + "c", new XAttribute("r", $"{(char)('A' + j)}{i + 1}"), new XAttribute("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), v)))))))));
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Write("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Alva\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Write("xl/worksheets/sheet1.xml", sheet.ToString());
            void Write(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
        }
        return stream.ToArray();
    }
    public static DataPackage Decode(byte[] bytes, string format)
    {
        if (bytes.Length > MaxBytes) throw new FormatException("Файл превышает 10 МБ.");
        if (format == "json") return JsonSerializer.Deserialize<DataPackage>(bytes, Json) ?? throw new JsonException("Пустой файл.");
        var rows = format == "csv" ? Csv(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')) : format == "xlsx" ? Workbook(bytes) : throw new FormatException("Неизвестный формат.");
        if (rows.Count < 2 || !rows[0].SequenceEqual(new[] { "kind", "id", "payload" }) || rows[1].Length != 3 || rows[1][0] != "metadata") throw new FormatException("Ожидаются столбцы kind,id,payload и строка metadata.");
        if (!Guid.TryParse(rows[1][1], out var teamId) || !int.TryParse(rows[1][2], out var version)) throw new FormatException("Некорректная metadata.");
        var records = new List<DataRow>();
        for (var i = 2; i < rows.Count; i++)
        {
            if (rows[i].Length != 3 || !Guid.TryParse(rows[i][1], out var id)) throw new FormatException($"Строка {i + 1}: нужны kind,id,payload.");
            try { records.Add(new(rows[i][0], id, JsonDocument.Parse(rows[i][2]).RootElement.Clone())); }
            catch (JsonException) { throw new FormatException($"Строка {i + 1}: некорректный JSON payload."); }
        }
        return new(version, teamId, records.ToArray());
    }
    private static List<string[]> Csv(string text)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false, afterQuote = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; afterQuote = true; } } else field.Append(c); continue; }
            if (c == '"' && field.Length == 0 && !afterQuote) { quoted = true; continue; }
            if (c == ',' || c is '\r' or '\n')
            {
                row.Add(field.ToString()); field.Clear(); afterQuote = false;
                if (c != ',') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; rows.Add(row.ToArray()); row.Clear(); if (rows.Count > MaxRows + 2) throw new FormatException("Слишком много строк."); } continue;
            }
            if (afterQuote || c == '"') throw new FormatException("Некорректные CSV кавычки."); field.Append(c);
        }
        if (quoted) throw new FormatException("Незакрытая CSV кавычка.");
        if (field.Length > 0 || row.Count > 0 || afterQuote) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
    private static List<string[]> Workbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 100 || zip.Entries.Any(x => x.Length > MaxBytes * 4L) || zip.Entries.Sum(x => x.Length) > MaxBytes * 4L || zip.Entries.Select(x => x.FullName).Distinct().Count() != zip.Entries.Count) throw new FormatException("Небезопасный или слишком большой XLSX архив.");
        XDocument Xml(ZipArchiveEntry entry) { using var input = entry.Open(); using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes * 4L }); return XDocument.Load(reader); }
        foreach (var entry in zip.Entries.Where(x => x.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            if (Xml(entry).Descendants().Any(x => x.Attribute("TargetMode")?.Value == "External")) throw new FormatException("Внешние ссылки XLSX запрещены.");
        if (zip.Entries.Any(x => x.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase))) throw new FormatException("Макросы запрещены.");
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = zip.GetEntry("xl/sharedStrings.xml") is { } sharedEntry ? Xml(sharedEntry).Descendants(s + "si").Select(x => string.Concat(x.Descendants(s + "t").Select(t => t.Value))).ToArray() : [];
        var sheets = zip.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && x.FullName.EndsWith(".xml", StringComparison.Ordinal)).ToArray();
        if (sheets.Length != 1) throw new FormatException("Нужен один лист с таблицей Alva.");
        var document = Xml(sheets[0]);
        if (document.Descendants(s + "f").Any()) throw new FormatException("Формулы XLSX запрещены.");
        var rows = new List<string[]>();
        foreach (var row in document.Descendants(s + "row"))
        {
            if (rows.Count >= MaxRows + 2) throw new FormatException("Слишком много строк.");
            var fields = new string[3]; var used = new HashSet<int>();
            foreach (var cell in row.Elements(s + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? ""; var column = reference.Length > 0 ? reference[0] - 'A' : -1;
                if (column is < 0 or > 2 || reference.Length < 2 || !int.TryParse(reference[1..], out _) || !used.Add(column)) throw new FormatException("Ожидаются только столбцы A, B, C.");
                fields[column] = cell.Attribute("t")?.Value switch
                {
                    "inlineStr" => string.Concat(cell.Descendants(s + "t").Select(x => x.Value)),
                    "s" when int.TryParse(cell.Element(s + "v")?.Value, out var index) && index >= 0 && index < shared.Length => shared[index],
                    "str" => cell.Element(s + "v")?.Value ?? "",
                    _ => throw new FormatException("Ячейки должны содержать текст, не числа или формулы.")
                };
            }
            if (used.Count != 3) throw new FormatException("В каждой строке нужны kind,id,payload.");
            rows.Add(fields);
        }
        return rows;
    }
}
