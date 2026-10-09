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
            if (!await db.ProjectTeams.AnyAsync(x => x.TeamId == teamId)) throw new ApiException(500, "Команда не найдена.");
            var project = new Project { WorkspaceId = await WorkspaceOf(db, teamId), Name = name, Description = request.Description ?? "" };
            db.Add(project);
            await LinkTeams(db, project, request.TeamIds);
            await db.SaveChangesAsync();
            return (await ListProjects(db, teamId)).Single(x => x.Id == project.Id);
        });
        group.MapPut("/projects/{projectId:guid}", async (Guid teamId, Guid projectId, ProjectRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "projects", "edit");
            var project = await FindProject(db, teamId, projectId);
            project.Name = AccessControl.RequiredName(request.Name);
            project.Description = request.Description ?? "";
            await LinkTeams(db, project, request.TeamIds);
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
            ApplySprint(sprint, request);
            db.Add(sprint);
            await db.SaveChangesAsync();
            return (await ListSprints(db, teamId)).Single(x => x.Id == sprint.Id);
        });
        group.MapPut("/sprints/{sprintId:guid}", async (Guid teamId, Guid sprintId, SprintRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "sprints", "edit");
            var sprint = await db.Sprints.SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == sprintId) ?? throw new ApiException(404, "Спринт не найден.");
            ApplySprint(sprint, request);
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

    private static async Task LinkTeams(AlvaDbContext db, Project project, Guid[]? teamIds)
    {
        var ids = (teamIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) throw new ApiException(400, "Проект должен быть связан хотя бы с одной командой.");
        if (await db.Teams.CountAsync(x => ids.Contains(x.Id) && x.WorkspaceId == project.WorkspaceId) != ids.Length) throw new ApiException(400, "Команды проекта должны принадлежать тому же рабочему пространству.");
        var existing = await db.ProjectTeams.Where(x => x.ProjectId == project.Id).ToListAsync();
        db.RemoveRange(existing.Where(x => !ids.Contains(x.TeamId)));
        foreach (var id in ids.Where(id => existing.All(x => x.TeamId != id))) db.Add(new ProjectTeam { ProjectId = project.Id, TeamId = id });
    }

    private static async Task<SprintDto[]> ListSprints(AlvaDbContext db, Guid teamId) =>
        await db.Sprints.AsNoTracking().Where(x => x.TeamId == teamId).OrderByDescending(x => x.StartDate)
            .Select(x => new SprintDto(x.Id, x.TeamId, x.Name, x.Goal, x.StartDate, x.EndDate, x.Status.ToString())).ToArrayAsync();

    private static void ApplySprint(Sprint sprint, SprintRequest request)
    {
        if (request.EndDate < request.StartDate) throw new ApiException(400, "Окончание спринта раньше начала.");
        if (!Enum.TryParse<SprintStatus>(request.Status, true, out var status) || !Enum.IsDefined(status)) throw new ApiException(400, "Неизвестный статус спринта.");
        sprint.Name = AccessControl.RequiredName(request.Name);
        sprint.Goal = request.Goal ?? "";
        sprint.StartDate = request.StartDate; sprint.EndDate = request.EndDate; sprint.Status = status;
    }

    internal static async Task<CapacityDto[]> Capacity(AlvaDbContext db, Guid teamId, DateOnly? from, DateOnly? to)
    {
        var start = from ?? DateOnly.FromDateTime(DateTime.Today);
        var end = to ?? start.AddDays(13);
        if (end < start) throw new ApiException(400, "Конец периода раньше начала.");
        if (end.DayNumber - start.DayNumber > 366) throw new ApiException(400, "Период не должен превышать 366 дней.");
        var people = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Kind == ActorKind.Person).OrderBy(x => x.Name).ToListAsync();
        var personIds = people.Select(x => x.Id).ToArray();
        var calendars = await db.Calendars.AsNoTracking().Where(x => personIds.Contains(x.ActorId)).ToListAsync();
        var calendarIds = calendars.Select(x => x.Id).ToArray();
        var exceptions = await db.CalendarExceptions.AsNoTracking().Where(x => calendarIds.Contains(x.CalendarId)).ToListAsync();
        var blocks = await db.RecurringBlocks.AsNoTracking().Where(x => calendarIds.Contains(x.CalendarId)).ToListAsync();
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Status != WorkStatus.Done && x.Status != WorkStatus.Cancelled).ToListAsync();
        var taskIds = tasks.Select(x => x.Id).ToArray();
        var assignments = await db.Assignments.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToListAsync();
        var allocated = new Dictionary<(Guid, DateOnly), decimal>();
        foreach (var task in tasks)
        {
            if (assignments.FirstOrDefault(x => x.TaskId == task.Id) is not { } assignment) continue;
            var person = ResolvePerson(assignment, people);
            var estimate = task.EstimateHours ?? 0m;
            if (person is null || estimate <= 0) continue;
            var windowStart = task.StartDate ?? task.EndDate ?? task.Deadline;
            var windowEnd = task.EndDate ?? task.Deadline ?? task.StartDate;
            if (windowStart is null || windowEnd is null) continue;
            if (windowEnd < windowStart) windowEnd = windowStart;
            var perDay = estimate / (windowEnd.Value.DayNumber - windowStart.Value.DayNumber + 1);
            for (var day = windowStart.Value; day <= windowEnd.Value; day = day.AddDays(1))
            {
                if (day < start || day > end) continue;
                var key = (person.Id, day);
                allocated[key] = allocated.GetValueOrDefault(key) + perDay;
            }
        }
        var result = new List<CapacityDto>();
        foreach (var person in people)
        {
            var calendar = calendars.FirstOrDefault(x => x.ActorId == person.Id);
            for (var day = start; day <= end; day = day.AddDays(1))
                result.Add(new CapacityDto(person.Id, person.Name, day, Available(calendar, exceptions, blocks, day), Math.Round(allocated.GetValueOrDefault((person.Id, day)), 2)));
        }
        return result.ToArray();
    }

    private static Actor? ResolvePerson(Assignment assignment, List<Actor> people) =>
        assignment.ResolvedPersonId is { } resolved ? people.FirstOrDefault(x => x.Id == resolved) : people.FirstOrDefault(x => x.Id == assignment.TargetId);

    private static decimal Available(WorkCalendar? calendar, List<CalendarException> exceptions, List<RecurringBlock> blocks, DateOnly day)
    {
        if (calendar is null) return 0m;
        var value = DayHours(calendar, day.DayOfWeek);
        if (exceptions.FirstOrDefault(x => x.CalendarId == calendar.Id && x.From <= day && day <= x.To) is { } exception) value = exception.AvailableHours;
        value -= blocks.Where(x => x.CalendarId == calendar.Id && x.DayOfWeek == day.DayOfWeek).Sum(x => x.Hours);
        return value < 0 ? 0 : value;
    }

    private static decimal DayHours(WorkCalendar calendar, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => calendar.Monday, DayOfWeek.Tuesday => calendar.Tuesday, DayOfWeek.Wednesday => calendar.Wednesday,
        DayOfWeek.Thursday => calendar.Thursday, DayOfWeek.Friday => calendar.Friday, DayOfWeek.Saturday => calendar.Saturday, _ => calendar.Sunday
    };

    internal static async Task<RiskDto[]> Risks(AlvaDbContext db, Guid teamId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Status != WorkStatus.Done && x.Status != WorkStatus.Cancelled).ToArrayAsync();
        var ids = tasks.Select(x => x.Id).ToArray();
        var assignments = await db.Assignments.AsNoTracking().Where(x => ids.Contains(x.TaskId)).ToListAsync();
        var people = await db.Actors.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Kind == ActorKind.Person).ToListAsync();
        var personIds = people.Select(x => x.Id).ToArray();
        var calendars = await db.Calendars.AsNoTracking().Where(x => personIds.Contains(x.ActorId)).ToDictionaryAsync(x => x.ActorId);
        var dependencies = await db.Dependencies.AsNoTracking().Where(x => ids.Contains(x.PredecessorId) || ids.Contains(x.SuccessorId)).ToListAsync();
        var estimates = TaskEndpoints.EffectiveEstimates(tasks);
        var deadlines = TaskEndpoints.EffectiveDeadlines(tasks);
        var load = new Dictionary<Guid, decimal>();
        foreach (var task in tasks)
        {
            if (assignments.FirstOrDefault(x => x.TaskId == task.Id) is not { } assignment) continue;
            var person = ResolvePerson(assignment, people);
            if (person is null) continue;
            load[person.Id] = load.GetValueOrDefault(person.Id) + (task.EstimateHours ?? estimates.GetValueOrDefault(task.Id));
        }
        var capacity = new Dictionary<Guid, decimal>();
        foreach (var person in people)
        {
            var weekly = calendars.TryGetValue(person.Id, out var calendar) ? calendar.Monday + calendar.Tuesday + calendar.Wednesday + calendar.Thursday + calendar.Friday : 40m;
            capacity[person.Id] = weekly <= 0 ? (load.GetValueOrDefault(person.Id) > 0 ? 2m : 0m) : load.GetValueOrDefault(person.Id) / weekly;
        }
        var result = new List<RiskDto>();
        foreach (var task in tasks)
        {
            var reasons = new List<string>();
            var assignment = assignments.FirstOrDefault(x => x.TaskId == task.Id);
            var person = assignment is null ? null : ResolvePerson(assignment, people);
            if (assignment is null) reasons.Add("Нет исполнителя");
            var deadline = deadlines.GetValueOrDefault(task.Id);
            var forecast = Forecast(today, estimates.GetValueOrDefault(task.Id));
            if (deadline is { } passed && passed < today) reasons.Add("Срок прошёл");
            else if (deadline is { } due && forecast > due) reasons.Add("Прогноз позже срока");
            if (person is not null && capacity.GetValueOrDefault(person.Id) > 1m) reasons.Add($"Перегрузка исполнителя: {person.Name}");
            if (dependencies.Where(x => x.SuccessorId == task.Id).Select(x => x.PredecessorId).ToArray() is { Length: > 0 } predecessors && tasks.Any(x => predecessors.Contains(x.Id) && x.Status != WorkStatus.Done)) reasons.Add("Зависимость не завершена");
            if (reasons.Count == 0) continue;
            var severe = (deadline is { } d && d < today) || (deadline is { } due2 && forecast > due2 && forecast.DayNumber - due2.DayNumber > 3);
            result.Add(new RiskDto(task.Id, task.Title, severe ? "High" : "Medium", reasons.ToArray(), forecast));
        }
        return result.OrderByDescending(x => x.Severity).ThenBy(x => x.ForecastDate).ThenBy(x => x.Title).ToArray();
    }

    private static DateOnly Forecast(DateOnly today, decimal estimate)
    {
        if (estimate <= 0) return today;
        var days = (int)Math.Ceiling(estimate / 8m);
        var date = today;
        var added = 0;
        while (added < days)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) added++;
        }
        return date;
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
        await EnsurePerson(db, teamId, actorId);
        return await db.Calendars.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actorId) ?? new WorkCalendar { ActorId = actorId };
    }

    private static async Task<WorkCalendar> EditableCalendar(AlvaDbContext db, Guid teamId, Guid actorId)
    {
        await EnsurePerson(db, teamId, actorId);
        if (await db.Calendars.SingleOrDefaultAsync(x => x.ActorId == actorId) is { } calendar) return calendar;
        var created = new WorkCalendar { ActorId = actorId };
        db.Add(created);
        return created;
    }

    private static async Task EnsurePerson(AlvaDbContext db, Guid teamId, Guid actorId)
    {
        if (!await db.Actors.AnyAsync(x => x.TeamId == teamId && x.Id == actorId && !x.Archived && x.Kind == ActorKind.Person)) throw new ApiException(404, "Человек не найден в команде.");
    }

    private static async Task<CalendarDto> ToCalendarDto(AlvaDbContext db, WorkCalendar calendar)
    {
        var exceptions = await db.CalendarExceptions.AsNoTracking().Where(x => x.CalendarId == calendar.Id).OrderBy(x => x.From).ToArrayAsync();
        var blocks = await db.RecurringBlocks.AsNoTracking().Where(x => x.CalendarId == calendar.Id).OrderBy(x => x.DayOfWeek).ToArrayAsync();
        return new CalendarDto(calendar.ActorId, calendar.TimeZone, calendar.Monday, calendar.Tuesday, calendar.Wednesday, calendar.Thursday, calendar.Friday, calendar.Saturday, calendar.Sunday,
            exceptions.Select(x => new CalendarExceptionDto(x.Id, x.From, x.To, x.AvailableHours, x.Reason)).ToArray(),
            blocks.Select(x => new RecurringBlockDto(x.Id, (int)x.DayOfWeek, x.Hours, x.Name)).ToArray());
    }
}
