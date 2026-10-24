using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Alva.Api;

public static class PlanningEndpoints
{
    public static void MapPlanning(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}").RequireAuthorization();

        group.MapGet("/projects", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "projects", "view");
            return await ListProjects(db, teamId);
        });
        group.MapPost("/projects", async (Guid teamId, ProjectRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "projects", "edit");
            var name = AccessControl.RequiredName(request.Name);
            var project = new Project { WorkspaceId = await WorkspaceOf(db, teamId), Name = name, Description = request.Description ?? "" };
            db.Add(project);
            await LinkTeams(db, project, teamId, request.TeamIds);
            await db.SaveChangesAsync();
            return (await ListProjects(db, teamId)).Single(x => x.Id == project.Id);
        });
        group.MapPut("/projects/{projectId:guid}", async (Guid teamId, Guid projectId, ProjectRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "projects", "edit");
            var project = await FindProject(db, teamId, projectId);
            project.Name = AccessControl.RequiredName(request.Name);
            project.Description = request.Description ?? "";
            await LinkTeams(db, project, teamId, request.TeamIds);
            await db.SaveChangesAsync();
            return (await ListProjects(db, teamId)).Single(x => x.Id == project.Id);
        });
        group.MapDelete("/projects/{projectId:guid}", async (Guid teamId, Guid projectId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "projects", "manage");
            var project = await FindProject(db, teamId, projectId);
            project.Archived = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapGet("/sprints", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "sprints", "view");
            return await ListSprints(db, teamId);
        });
        group.MapPost("/sprints", async (Guid teamId, SprintRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "sprints", "edit");
            var sprint = new Sprint { TeamId = teamId };
            await ApplySprint(db, sprint, request);
            db.Add(sprint);
            await db.SaveChangesAsync();
            return (await ListSprints(db, teamId)).Single(x => x.Id == sprint.Id);
        });
        group.MapPost("/sprints/{sprintId:guid}/close", async (Guid teamId, Guid sprintId, SprintCloseRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "sprints", "edit");
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            await Close(db, teamId, sprintId, request, user.UserId());
            return (await ListSprints(db, teamId)).Single(x => x.Id == sprintId);
        });
        group.MapPut("/sprints/{sprintId:guid}", async (Guid teamId, Guid sprintId, SprintRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "sprints", "edit");
            var sprint = await db.Sprints.SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == sprintId) ?? throw new ApiException(404, "Спринт не найден.");
            await ApplySprint(db, sprint, request);
            await db.SaveChangesAsync();
            return (await ListSprints(db, teamId)).Single(x => x.Id == sprint.Id);
        });
    }

    public static void MapCapacity(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}").RequireAuthorization();
        group.MapGet("/capacity", async (Guid teamId, DateOnly? from, DateOnly? to, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "view");
            return await Capacity(db, teamId, from, to);
        });
        group.MapGet("/capacity/demand", async (Guid teamId, DateOnly? from, DateOnly? to, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "view");
            return await Demand(db, teamId, from, to);
        });
        group.MapGet("/risks", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "analytics", "view");
            return await Risks(db, teamId);
        });
        group.MapGet("/dashboard", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "analytics", "view");
            return await Dashboard(db, teamId);
        });
        var calendars = app.MapGroup("/api/teams/{teamId:guid}/actors/{actorId:guid}/calendar").RequireAuthorization();
        calendars.MapGet("", async (Guid teamId, Guid actorId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "view");
            var calendar = await Calendar(db, teamId, actorId);
            return await ToCalendarDto(db, calendar);
        });
        calendars.MapPut("", async (Guid teamId, Guid actorId, CalendarRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "edit");
            var calendar = await EditableCalendar(db, teamId, actorId);
            if (string.IsNullOrWhiteSpace(request.TimeZone) || request.TimeZone.Trim().Length > 64) throw new ApiException(400, "Укажите часовой пояс IANA.");
            decimal[] hours = [request.Monday, request.Tuesday, request.Wednesday, request.Thursday, request.Friday, request.Saturday, request.Sunday];
            if (hours.Any(x => x < 0 || x > 24)) throw new ApiException(400, "Часы дня должны быть от 0 до 24.");
            calendar.TimeZone = request.TimeZone.Trim();
            calendar.Monday = request.Monday; calendar.Tuesday = request.Tuesday; calendar.Wednesday = request.Wednesday; calendar.Thursday = request.Thursday;
            calendar.Friday = request.Friday; calendar.Saturday = request.Saturday; calendar.Sunday = request.Sunday;
            await db.SaveChangesAsync();
            return await ToCalendarDto(db, calendar);
        });
        calendars.MapPost("/exceptions", async (Guid teamId, Guid actorId, CalendarExceptionRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "edit");
            var calendar = await EditableCalendar(db, teamId, actorId);
            if (request.To < request.From) throw new ApiException(400, "Окончание отсутствия раньше начала.");
            if (request.AvailableHours is < 0 or > 24) throw new ApiException(400, "Доступные часы должны быть от 0 до 24.");
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) throw new ApiException(400, "Укажите причину отсутствия.");
            db.Add(new CalendarException { CalendarId = calendar.Id, From = request.From, To = request.To, AvailableHours = request.AvailableHours, Reason = request.Reason.Trim() });
            await db.SaveChangesAsync();
            return await ToCalendarDto(db, calendar);
        });
        calendars.MapDelete("/exceptions/{exceptionId:guid}", async (Guid teamId, Guid actorId, Guid exceptionId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "edit");
            var calendar = await EditableCalendar(db, teamId, actorId);
            var exception = await db.CalendarExceptions.SingleOrDefaultAsync(x => x.Id == exceptionId && x.CalendarId == calendar.Id) ?? throw new ApiException(404, "Исключение не найдено.");
            db.Remove(exception);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        calendars.MapPost("/blocks", async (Guid teamId, Guid actorId, RecurringBlockRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "edit");
            var calendar = await EditableCalendar(db, teamId, actorId);
            if (request.DayOfWeek is < 0 or > 6) throw new ApiException(400, "День недели должен быть от 0 (воскресенье) до 6 (суббота).");
            if (request.Hours is < 0 or > 24) throw new ApiException(400, "Часы блока должны быть от 0 до 24.");
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) throw new ApiException(400, "Укажите название блока.");
            db.Add(new RecurringBlock { CalendarId = calendar.Id, DayOfWeek = (DayOfWeek)request.DayOfWeek, Hours = request.Hours, Name = request.Name.Trim() });
            await db.SaveChangesAsync();
            return await ToCalendarDto(db, calendar);
        });
        calendars.MapDelete("/blocks/{blockId:guid}", async (Guid teamId, Guid actorId, Guid blockId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "capacity", "edit");
            var calendar = await EditableCalendar(db, teamId, actorId);
            var block = await db.RecurringBlocks.SingleOrDefaultAsync(x => x.Id == blockId && x.CalendarId == calendar.Id) ?? throw new ApiException(404, "Блок не найден.");
            db.Remove(block);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<ProjectDto[]> ListProjects(AlvaDbContext db, Guid teamId)
    {
        var projectIds = await db.ProjectTeams.Where(x => x.TeamId == teamId).Select(x => x.ProjectId).ToArrayAsync();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.Id) && !x.Archived).OrderBy(x => x.Name).ToArrayAsync();
        var links = await db.ProjectTeams.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToArrayAsync();
        return projects.Select(x => new ProjectDto(x.Id, x.Name, x.Description, links.Where(l => l.ProjectId == x.Id).Select(l => l.TeamId).ToArray())).ToArray();
    }

    private static async Task<Project> FindProject(AlvaDbContext db, Guid teamId, Guid projectId)
    {
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId && !x.Archived) ?? throw new ApiException(404, "Проект не найден.");
        if (!await db.ProjectTeams.AnyAsync(x => x.ProjectId == projectId && x.TeamId == teamId)) throw new ApiException(404, "Проект не найден.");
        return project;
    }

    private static async Task<Guid> WorkspaceOf(AlvaDbContext db, Guid teamId) =>
        (await db.Teams.FindAsync(teamId) ?? throw new ApiException(404, "Команда не найдена.")).WorkspaceId;

    private static async Task LinkTeams(AlvaDbContext db, Project project, Guid teamId, Guid[]? teamIds)
    {
        var ids = (teamIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) throw new ApiException(400, "Проект должен быть связан хотя бы с одной командой.");
        // Иначе проект пропадает из списка команды, которая его сохраняет.
        if (!ids.Contains(teamId)) throw new ApiException(400, "Проект должен оставаться связан с командой, в которой его редактируют.");
        if (await db.Teams.CountAsync(x => ids.Contains(x.Id) && x.WorkspaceId == project.WorkspaceId) != ids.Length) throw new ApiException(400, "Команды проекта должны принадлежать тому же рабочему пространству.");
        var existing = await db.ProjectTeams.Where(x => x.ProjectId == project.Id).ToListAsync();
        db.RemoveRange(existing.Where(x => !ids.Contains(x.TeamId)));
        foreach (var id in ids.Where(id => existing.All(x => x.TeamId != id))) db.Add(new ProjectTeam { ProjectId = project.Id, TeamId = id });
    }

    private static async Task<SprintDto[]> ListSprints(AlvaDbContext db, Guid teamId) =>
        [.. (await db.Sprints.AsNoTracking().Where(x => x.TeamId == teamId).OrderByDescending(x => x.StartDate).ToArrayAsync()).Select(ToDto)];

    private static SprintDto ToDto(Sprint x) => new(x.Id, x.TeamId, x.Name, x.Goal, x.StartDate, x.EndDate, x.Status.ToString(),
        x.CommitmentTasks is { } committed ? new SprintSnapshotDto(committed, x.CommitmentHours ?? 0, x.CommitmentDoneHours ?? 0, x.CommittedAt ?? default) : null,
        x.ResultTasks is { } result ? new SprintSnapshotDto(result, x.ResultHours ?? 0, x.ResultDoneHours ?? 0, x.ClosedAt ?? default) : null);

    private static async Task ApplySprint(AlvaDbContext db, Sprint sprint, SprintRequest request)
    {
        if (request.EndDate < request.StartDate) throw new ApiException(400, "Окончание спринта раньше начала.");
        if (!Enum.TryParse<SprintStatus>(request.Status, true, out var status) || !Enum.IsDefined(status)) throw new ApiException(400, "Неизвестный статус спринта.");
        var before = sprint.Status;
        sprint.Name = AccessControl.RequiredName(request.Name);
        sprint.Goal = request.Goal ?? "";
        sprint.StartDate = request.StartDate; sprint.EndDate = request.EndDate; sprint.Status = status;
        await Snapshot(db, sprint, before);
    }

    /// <summary>
    /// Снимки при смене статуса: старт фиксирует обязательство (один раз), закрытие — итог; переоткрытый спринт итога не
    /// имеет. Цифры берутся из задач команды на момент перехода и дальше не пересчитываются.
    /// </summary>
    public static async Task Snapshot(AlvaDbContext db, Sprint sprint, SprintStatus before)
    {
        if (sprint.Status == before && (sprint.Status != SprintStatus.Active || sprint.CommitmentTasks is not null)) return;
        if (sprint.Status != SprintStatus.Completed) { sprint.ResultTasks = null; sprint.ResultHours = sprint.ResultDoneHours = null; sprint.ClosedAt = null; }
        if (sprint.Status == SprintStatus.Planned || (sprint.Status == SprintStatus.Completed && sprint.ResultTasks is not null)) return;
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.TeamId == sprint.TeamId && !t.Archived && (t.StartDate != null || t.EndDate != null))
            .Select(t => new { t.EstimateHours, t.StartDate, t.EndDate, t.Status }).ToListAsync();
        var snapshot = SprintWindows.Snapshot(tasks.Select(t => (t.EstimateHours, t.StartDate, t.EndDate, t.Status == WorkStatus.Done, t.Status == WorkStatus.Cancelled)),
            sprint.StartDate, sprint.EndDate, DateTimeOffset.UtcNow);
        if (sprint.CommitmentTasks is null && sprint.Status is SprintStatus.Active or SprintStatus.Completed && before != SprintStatus.Completed)
        {
            sprint.CommitmentTasks = snapshot.Tasks; sprint.CommitmentHours = snapshot.Hours; sprint.CommitmentDoneHours = snapshot.DoneHours; sprint.CommittedAt = snapshot.At;
        }
        if (sprint.Status == SprintStatus.Completed)
        {
            sprint.ResultTasks = snapshot.Tasks; sprint.ResultHours = snapshot.Hours; sprint.ResultDoneHours = snapshot.DoneHours; sprint.ClosedAt = snapshot.At;
        }
    }


    /// <summary>
    /// Закрытие спринта с решением по каждой незавершённой задаче, одной транзакцией: отметки «готова», затем снимок итога
    /// (в нём остаток виден как незакрытые часы), затем перенос остатка в следующий спринт или в пул.
    /// </summary>
    private static async Task Close(AlvaDbContext db, Guid teamId, Guid sprintId, SprintCloseRequest request, Guid userId)
    {
        var sprint = await db.Sprints.SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == sprintId) ?? throw new ApiException(404, "Спринт не найден.");
        if (sprint.Status == SprintStatus.Completed) throw new ApiException(400, "Спринт уже закрыт.");
        var decisions = new Dictionary<Guid, string>();
        foreach (var item in request.Tasks ?? [])
            if (!SprintCarry.Known(item.Action) || !decisions.TryAdd(item.TaskId, item.Action)) throw new ApiException(400, "Неизвестное или повторное решение по задаче.");
        var open = (await db.Tasks.Where(t => t.TeamId == teamId && !t.Archived && t.Status != WorkStatus.Done && t.Status != WorkStatus.Cancelled && (t.StartDate != null || t.EndDate != null)).ToListAsync())
            .Where(t => SprintWindows.Overlaps(t.StartDate, t.EndDate, sprint.StartDate, sprint.EndDate)).ToList();
        if (open.Any(t => !decisions.ContainsKey(t.Id))) throw new ApiException(400, "Решите, что делать с каждой незавершённой задачей спринта.");
        // Перенос в следующий спринт и возврат в пул меняют или очищают даты — фиксированную задачу так нельзя.
        foreach (var task in open.Where(t => t.DatesFixed && (decisions[t.Id] is SprintCarry.Next or SprintCarry.Backlog)))
            throw new ApiException(400, $"Даты задачи «{task.Title}» зафиксированы — при закрытии спринта её нельзя перенести в следующий спринт или вернуть в пул. Снимите фиксацию заранее.");
        Sprint? next = null;
        if (decisions.Values.Contains(SprintCarry.Next))
            next = await db.Sprints.SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == request.NextSprintId && x.Id != sprintId && x.Status != SprintStatus.Completed)
                ?? throw new ApiException(400, "Выберите незакрытый спринт команды для переноса.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var task in open.Where(t => decisions[t.Id] == SprintCarry.Done))
        {
            task.Status = WorkStatus.Done; task.Version++;
            db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = $"Отмечена готовой при закрытии спринта «{sprint.Name}»" });
        }
        await db.SaveChangesAsync();
        var before = sprint.Status;
        sprint.Status = SprintStatus.Completed;
        await Snapshot(db, sprint, before);
        var parents = await db.Tasks.Where(t => t.TeamId == teamId).ToDictionaryAsync(t => t.Id, t => t.ParentId);
        foreach (var task in SprintCarry.LeavesFirst(open.Where(t => decisions[t.Id] != SprintCarry.Done), t => t.Id, id => parents.GetValueOrDefault(id)))
        {
            if (decisions[task.Id] == SprintCarry.Next)
            {
                (task.StartDate, task.EndDate) = SprintCarry.Move(task.StartDate, task.EndDate, next!.StartDate);
                db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = $"Перенесена из «{sprint.Name}» в «{next.Name}»" });
            }
            else
            {
                task.StartDate = task.EndDate = null;
                db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = $"Возвращена в пул при закрытии «{sprint.Name}»" });
            }
            task.Version++;
            await TaskEndpoints.KeepParentsAroundChildren(db, teamId, task, userId);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static (DateOnly Start, DateOnly End) Period(DateOnly? from, DateOnly? to)
    {
        var start = from ?? DateOnly.FromDateTime(DateTime.Today);
        var end = to ?? start.AddDays(13);
        if (end < start) throw new ApiException(400, "Конец периода раньше начала.");
        if (end.DayNumber - start.DayNumber > 366) throw new ApiException(400, "Период не должен превышать 366 дней.");
        return (start, end);
    }

    /// <summary>Открытые задачи команды с первым назначением (если есть) и плановым окном — датами задачи.</summary>
    private static async Task<List<(TaskCard Task, Assignment? Assignment, DateOnly? Start, DateOnly? End)>> OpenWork(AlvaDbContext db, Guid teamId, bool includeClosed = false)
    {
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && (includeClosed || (x.Status != WorkStatus.Done && x.Status != WorkStatus.Cancelled))).ToListAsync();
        var taskIds = tasks.Select(x => x.Id).ToArray();
        var assignments = (await db.Assignments.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToListAsync()).ToDictionary(x => x.TaskId);
        return [.. tasks.Select(task => (task, assignments.GetValueOrDefault(task.Id), task.StartDate, task.EndDate))];
    }

    /// <summary>Календари людей команды вместе с общими графиками их подкоманд — так их видят ёмкость и риски.</summary>
    internal static async Task<(List<Actor> People, Func<Guid, CalendarDto> CalendarOf)> PeopleCalendars(AlvaDbContext db, Guid teamId)
    {
        var people = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Kind == ActorKind.Person).OrderBy(x => x.Name).ToListAsync();
        var departments = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Kind == ActorKind.Department).ToDictionaryAsync(x => x.Id, x => x.ParentId);
        var personIds = people.Select(x => x.Id).ToArray();
        var memberships = await db.ActorDepartments.AsNoTracking().Where(x => personIds.Contains(x.ActorId)).ToListAsync();
        var owners = personIds.Concat(departments.Keys).ToArray();
        var calendars = await db.Calendars.AsNoTracking().Where(x => owners.Contains(x.ActorId)).ToListAsync();
        var calendarIds = calendars.Select(x => x.Id).ToArray();
        var exceptions = await db.CalendarExceptions.AsNoTracking().Where(x => calendarIds.Contains(x.CalendarId)).ToListAsync();
        var blocks = await db.RecurringBlocks.AsNoTracking().Where(x => calendarIds.Contains(x.CalendarId)).ToListAsync();
        var dtos = calendars.ToDictionary(x => x.ActorId, x => ToCalendarDto(x, exceptions, blocks));
        CalendarDto CalendarOf(Guid id)
        {
            var own = memberships.Where(x => x.ActorId == id).Select(x => x.DepartmentId).Where(departments.ContainsKey);
            var shared = CapacityMath.DepartmentsWithParents(own, x => departments.TryGetValue(x, out var parent) && parent is { } p && departments.ContainsKey(p) ? p : null);
            return CapacityMath.Combine(dtos.GetValueOrDefault(id) ?? CapacityMath.DefaultCalendar(id), shared.Select(x => dtos.GetValueOrDefault(x)).OfType<CalendarDto>());
        }
        return (people, CalendarOf);
    }

    internal static async Task<CapacityDto[]> Capacity(AlvaDbContext db, Guid teamId, DateOnly? from, DateOnly? to)
    {
        var (start, end) = Period(from, to);
        var (people, calendarOf) = await PeopleCalendars(db, teamId);
        var work = new List<CapacityWork>();
        foreach (var (task, assignment, taskStart, taskEnd) in await OpenWork(db, teamId))
            if (SprintWindows.Planned(taskStart, taskEnd) && assignment is not null && ResolvePerson(assignment, people) is { } person) work.Add(new CapacityWork(person.Id, task.EstimateHours ?? 0m, taskStart, taskEnd, task.Deadline));
        return CapacityMath.Build(people.Select(x => (x.Id, x.Name)), calendarOf, work, start, end);
    }

    /// <summary>Потребность ролей и подкоманд: задачи, назначенные на них без выбранного человека.</summary>
    internal static async Task<CapacityDemandDto[]> Demand(AlvaDbContext db, Guid teamId, DateOnly? from, DateOnly? to)
    {
        var (start, end) = Period(from, to);
        var targets = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Kind != ActorKind.Person).ToDictionaryAsync(x => x.Id);
        var work = new List<DemandWork>();
        foreach (var (task, assignment, taskStart, taskEnd) in await OpenWork(db, teamId))
            if (SprintWindows.Planned(taskStart, taskEnd) && assignment is { ResolvedPersonId: null } && targets.TryGetValue(assignment.TargetId, out var target))
                work.Add(new DemandWork(target.Id, target.Name, target.Kind.ToString(), task.EstimateHours ?? 0m, taskStart, taskEnd, task.Deadline));
        return CapacityMath.Demand(work, start, end);
    }

    private static Actor? ResolvePerson(Assignment assignment, List<Actor> people) =>
        assignment.ResolvedPersonId is { } resolved ? people.FirstOrDefault(x => x.Id == resolved) : people.FirstOrDefault(x => x.Id == assignment.TargetId);

    internal static async Task<RiskDto[]> Risks(AlvaDbContext db, Guid teamId)
    {
        var (people, calendarOf) = await PeopleCalendars(db, teamId);
        var targets = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Kind != ActorKind.Person).ToDictionaryAsync(x => x.Id, x => x.Name);
        var work = await OpenWork(db, teamId, includeClosed: true);
        var ids = work.Select(x => x.Task.Id).ToArray();
        var dependencies = await db.Dependencies.AsNoTracking().Where(x => ids.Contains(x.SuccessorId)).Select(x => new RiskDependency(x.PredecessorId, x.SuccessorId, x.Kind.ToString(), x.LagDays)).ToListAsync();
        var tasks = work.Select(x =>
        {
            var person = x.Assignment is null ? null : ResolvePerson(x.Assignment, people);
            var target = x.Assignment is not null && person is null ? targets.GetValueOrDefault(x.Assignment.TargetId) : null;
            return new RiskTask(x.Task.Id, x.Task.ParentId, x.Task.Title, x.Task.EstimateHours, x.Start, x.End, x.Task.Deadline, person?.Id, person?.Name, target, x.Task.Status.ToString(), x.Task.RemainingHours, x.Task.Priority.ToString(), x.Task.BacklogRank);
        }).ToArray();
        return RiskMath.Assess(tasks, dependencies, calendarOf, DateOnly.FromDateTime(DateTime.Today));
    }

    private static async Task<DashboardDto> Dashboard(AlvaDbContext db, Guid teamId)
    {
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived).ToArrayAsync();
        var sprints = await db.Sprints.AsNoTracking().CountAsync(x => x.TeamId == teamId && x.Status == SprintStatus.Active);
        var risks = await Risks(db, teamId);
        return new DashboardDto(tasks.Length, tasks.Count(x => x.Status == WorkStatus.Done), sprints, tasks.Sum(x => x.EstimateHours ?? 0m), risks);
    }

    private static async Task<WorkCalendar> Calendar(AlvaDbContext db, Guid teamId, Guid actorId)
    {
        await EnsureCalendarOwner(db, teamId, actorId);
        return await db.Calendars.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actorId) ?? new WorkCalendar { ActorId = actorId };
    }

    private static async Task<WorkCalendar> EditableCalendar(AlvaDbContext db, Guid teamId, Guid actorId)
    {
        await EnsureCalendarOwner(db, teamId, actorId);
        if (await db.Calendars.SingleOrDefaultAsync(x => x.ActorId == actorId) is { } calendar) return calendar;
        var created = new WorkCalendar { ActorId = actorId };
        db.Add(created);
        return created;
    }

    private static async Task EnsureCalendarOwner(AlvaDbContext db, Guid teamId, Guid actorId)
    {
        if (!await db.Actors.AnyAsync(x => x.TeamId == teamId && x.Id == actorId && !x.Archived && (x.Kind == ActorKind.Person || x.Kind == ActorKind.Department))) throw new ApiException(404, "Человек или подкоманда не найдены в команде.");
    }

    private static async Task<CalendarDto> ToCalendarDto(AlvaDbContext db, WorkCalendar calendar)
    {
        var exceptions = await db.CalendarExceptions.AsNoTracking().Where(x => x.CalendarId == calendar.Id).ToListAsync();
        var blocks = await db.RecurringBlocks.AsNoTracking().Where(x => x.CalendarId == calendar.Id).ToListAsync();
        return ToCalendarDto(calendar, exceptions, blocks);
    }

    private static CalendarDto ToCalendarDto(WorkCalendar calendar, List<CalendarException> exceptions, List<RecurringBlock> blocks) =>
        new(calendar.ActorId, calendar.TimeZone, calendar.Monday, calendar.Tuesday, calendar.Wednesday, calendar.Thursday, calendar.Friday, calendar.Saturday, calendar.Sunday,
            exceptions.Where(x => x.CalendarId == calendar.Id).OrderBy(x => x.From).Select(x => new CalendarExceptionDto(x.Id, x.From, x.To, x.AvailableHours, x.Reason)).ToArray(),
            blocks.Where(x => x.CalendarId == calendar.Id).OrderBy(x => x.DayOfWeek).Select(x => new RecurringBlockDto(x.Id, (int)x.DayOfWeek, x.Hours, x.Name)).ToArray());
}
