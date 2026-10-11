using System.Net;
using System.Text.Json;
using Alva.Client.Models;
using Alva.Contracts;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>
/// Demo-реализация: данные живут в localStorage браузера, сеть не используется.
/// Живёт в проекте <c>Alva.Client.Demo</c> — продовая сборка этот код не содержит.
/// </summary>
public sealed class ApiServiceLocalStorage(IJSRuntime js) : IApiService
{
    private const string DataKey = "alva-demo-data";
    private const string WorkspaceKey = "alva-workspace";
    private const string TeamKey = "alva-team";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DemoStore? _store;
    private string _displayName = "Демо-пользователь";

    public UserInfo? User { get; private set; }
    public SpaceInfo[] Workspaces { get; private set; } = [];
    public TeamInfo[] Teams { get; private set; } = [];
    public Guid? WorkspaceId { get; private set; }
    public Guid? TeamId { get; private set; }
    public string TeamName => Teams.FirstOrDefault(t => t.Id == TeamId)?.Name ?? "Выбрать команду";
    public bool Initialized { get; private set; }
    public event Action? Changed;

    public string TeamPath(string resource) => $"api/teams/{TeamId}/{resource}";

    public async Task Initialize()
    {
        if (Initialized) return;
        var store = await Load();
        User = new UserInfo(DemoIds.User, "demo@alva.local", _displayName);
        Workspaces = store.Workspaces.Select(w => new SpaceInfo(w.Id, w.Name)).ToArray();
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", WorkspaceKey);
        var id = Guid.TryParse(stored, out var parsed) && Workspaces.Any(x => x.Id == parsed) ? parsed : Workspaces.FirstOrDefault()?.Id;
        if (id.HasValue) await SelectWorkspace(id.Value);
        Initialized = true;
        Changed?.Invoke();
    }

    public async Task Authenticate(string endpoint, object input)
    {
        var name = ReadString(input, "displayName");
        if (!string.IsNullOrWhiteSpace(name)) _displayName = name!;
        Initialized = false;
        await Initialize();
    }

    public async Task Logout()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", DataKey);
        await js.InvokeVoidAsync("localStorage.removeItem", WorkspaceKey);
        await js.InvokeVoidAsync("localStorage.removeItem", TeamKey);
        _store = null;
        User = null; Workspaces = []; Teams = []; WorkspaceId = TeamId = null; Initialized = true;
        Changed?.Invoke();
    }

    public async Task LoadWorkspaces()
    {
        var store = await Load();
        Workspaces = store.Workspaces.Select(w => new SpaceInfo(w.Id, w.Name)).ToArray();
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", WorkspaceKey);
        var id = Guid.TryParse(stored, out var parsed) && Workspaces.Any(x => x.Id == parsed) ? parsed : Workspaces.FirstOrDefault()?.Id;
        if (id.HasValue) await SelectWorkspace(id.Value);
    }

    public async Task SelectWorkspace(Guid id)
    {
        var store = await Load();
        WorkspaceId = id;
        await js.InvokeVoidAsync("localStorage.setItem", WorkspaceKey, id.ToString());
        Teams = store.Teams.Where(t => t.WorkspaceId == id).Select(t => new TeamInfo(t.Id, t.Name)).ToArray();
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", TeamKey);
        TeamId = Guid.TryParse(stored, out var parsed) && Teams.Any(x => x.Id == parsed) ? parsed : Teams.FirstOrDefault()?.Id;
        Changed?.Invoke();
    }

    public async Task SelectTeam(Guid id)
    {
        TeamId = id;
        await js.InvokeVoidAsync("localStorage.setItem", TeamKey, id.ToString());
        Changed?.Invoke();
    }

    public async Task RefreshUser()
    {
        User = new UserInfo(DemoIds.User, "demo@alva.local", _displayName);
        Changed?.Invoke();
        await Task.CompletedTask;
    }

    public async Task Import(string format, Stream stream)
    {
        // Демо не имеет серверного хранилища: файл читается, чтобы проверить поток, и не применяется.
        using var reader = new StreamReader(stream);
        await reader.ReadToEndAsync();
        await Load();
    }

    public async Task<T> Get<T>(string path)
    {
        var store = await Load();
        var (resource, query) = SplitQuery(path);
        var s = resource.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var today = DateOnly.FromDateTime(DateTime.Today);
        object result = s switch
        {
            ["api", "auth", "me"] => User ?? new UserInfo(DemoIds.User, "demo@alva.local", _displayName),
            ["api", "workspaces"] => store.Workspaces.ToArray(),
            ["api", "workspaces", var w, "teams"] => store.Teams.Where(t => t.WorkspaceId == Id(w)).Select(t => new TeamInfo(t.Id, t.Name)).ToArray(),
            ["api", "workspaces", _, "members"] => store.Members.ToArray(),
            ["api", "teams", var t, "projects"] => store.Projects.Where(p => p.TeamIds.Contains(Id(t))).ToArray(),
            ["api", "teams", _, "actors"] => store.Actors.ToArray(),
            ["api", "teams", var t, "sprints"] => store.Sprints.Where(x => store.SprintTeam.GetValueOrDefault(x.Id) == Id(t)).ToArray(),
            ["api", "teams", var t, "tasks"] => WithRollup(WithSprints(store, store.Tasks.Where(x => x.TeamId == Id(t)).ToArray())),
            ["api", "teams", var t, "tasks", "dependencies"] => TeamDependencies(store, Id(t)),
            ["api", "teams", _, "tasks", var id, "dependencies"] => store.Dependencies.Where(d => d.PredecessorId == Id(id) || d.SuccessorId == Id(id)).ToArray(),
            ["api", "teams", _, "tasks", var id, "history"] => History(store, Id(id)),
            ["api", "teams", _, "actors", var a, "calendar"] => Calendar(store, Id(a)),
            ["api", "teams", var t, "capacity"] => Capacity(store, Id(t), QueryDate(query, "from", today), QueryDate(query, "to", today.AddDays(13))),
            ["api", "teams", var t, "capacity", "demand"] => Demand(store, Id(t), QueryDate(query, "from", today), QueryDate(query, "to", today.AddDays(13))),
            ["api", "teams", var t, "risks"] => Risks(store, Id(t), today),
            _ => throw new NotSupportedException($"Demo: GET {path} не поддерживается.")
        };
        return (T)result;
    }

    private static (string Resource, Dictionary<string, string> Query) SplitQuery(string path)
    {
        var index = path.IndexOf('?', StringComparison.Ordinal);
        if (index < 0) return (path, []);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in path[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            query[parts[0]] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
        }
        return (path[..index], query);
    }

    private static DateOnly QueryDate(Dictionary<string, string> query, string key, DateOnly fallback) =>
        query.TryGetValue(key, out var raw) && DateOnly.TryParse(raw, out var parsed) ? parsed : fallback;

    private static DependencyDto[] TeamDependencies(DemoStore store, Guid teamId)
    {
        var ids = store.Tasks.Where(t => t.TeamId == teamId).Select(t => t.Id).ToHashSet();
        return [.. store.Dependencies.Where(d => ids.Contains(d.PredecessorId) && ids.Contains(d.SuccessorId))];
    }

    /// <summary>Новая связь с теми же проверками, что в API: без петель, повторов и чужих задач.</summary>
    private static DependencyDto AddDependency(DemoStore store, object value)
    {
        var request = Read<DependencyRequest>(value);
        if (request.PredecessorId == request.SuccessorId) throw new InvalidOperationException("Задача не может зависеть от самой себя.");
        if (!store.Tasks.Any(t => t.Id == request.PredecessorId) || !store.Tasks.Any(t => t.Id == request.SuccessorId)) throw new InvalidOperationException("Задача не найдена.");
        if (store.Dependencies.Any(d => d.PredecessorId == request.PredecessorId && d.SuccessorId == request.SuccessorId)) throw new InvalidOperationException("Зависимость уже существует.");
        var kind = request.Kind.ToUpperInvariant() is "FS" or "SS" or "FF" or "SF" ? request.Kind.ToUpperInvariant() : throw new InvalidOperationException("Неизвестный вид зависимости.");
        var dependency = new DependencyDto(Guid.NewGuid(), request.PredecessorId, request.SuccessorId, kind, request.LagDays);
        store.Dependencies.Add(dependency);
        return dependency;
    }

    private static HistoryDto[] History(DemoStore store, Guid taskId) =>
        store.History.TryGetValue(taskId, out var entries) ? [.. entries] : [];

    /// <summary>Спринты задач по их датам — тем же правилом, что кэш API; спринты команды берутся один раз.</summary>
    private static TaskModel[] WithSprints(DemoStore store, TaskModel[] tasks)
    {
        var sprints = store.Sprints.ToLookup(s => store.SprintTeam.GetValueOrDefault(s.Id));
        foreach (var task in tasks)
            task.SprintIds = [.. SprintWindows.Of(task.StartDate, task.EndDate, sprints[task.TeamId], s => s.StartDate, s => s.EndDate).Select(s => s.Id)];
        return tasks;
    }

    /// <summary>Оценка и срок с подъёмом по дереву, как их отдаёт API.</summary>
    private static TaskModel[] WithRollup(TaskModel[] tasks)
    {
        var children = tasks.Where(t => t.ParentId is not null).ToLookup(t => t.ParentId!.Value);
        var done = new HashSet<Guid>();
        void Resolve(TaskModel task)
        {
            if (!done.Add(task.Id)) return;
            foreach (var child in children[task.Id]) Resolve(child);
            task.EffectiveEstimateHours = task.EstimateHours ?? children[task.Id].Sum(c => c.EffectiveEstimateHours);
            task.EffectiveDeadline = children[task.Id].Select(c => c.EffectiveDeadline).Append(task.Deadline).Max();
        }
        foreach (var task in tasks) Resolve(task);
        return tasks;
    }

    private static CalendarDto Calendar(DemoStore store, Guid actorId) =>
        store.Calendars.FirstOrDefault(c => c.ActorId == actorId) ?? CapacityMath.DefaultCalendar(actorId);

    /// <summary>Открытые задачи команды с плановым окном — датами задачи (как в API).</summary>
    private static IEnumerable<(TaskModel Task, DateOnly? Start, DateOnly? End)> OpenTasks(DemoStore store, Guid teamId) =>
        store.Tasks.Where(t => t.TeamId == teamId && t.Status is not ("Done" or "Cancelled")).Select(t => (t, t.StartDate, t.EndDate));

    private static CapacityDemandDto[] Demand(DemoStore store, Guid teamId, DateOnly from, DateOnly to)
    {
        var targets = store.Actors.Where(a => a.Kind != "Person").ToDictionary(a => a.Id);
        var work = OpenTasks(store, teamId)
            .Where(x => x.Task.ResolvedPersonId is null && x.Task.AssignmentTargetId is { } id && targets.ContainsKey(id))
            .Select(x => (Task: x, Target: targets[x.Task.AssignmentTargetId!.Value]))
            .Select(x => new DemandWork(x.Target.Id, x.Target.Name, x.Target.Kind, x.Task.Task.EstimateHours ?? 0m, x.Task.Start, x.Task.End, x.Task.Task.Deadline));
        return CapacityMath.Demand(work, from, to);
    }

    private static CapacityDto[] Capacity(DemoStore store, Guid teamId, DateOnly from, DateOnly to)
    {
        var people = store.Actors.Where(a => a.Kind == "Person").ToArray();
        var work = OpenTasks(store, teamId).Select(x => (x, Person: PersonOf(store, x.Task))).Where(w => w.Person.HasValue)
            .Select(w => new CapacityWork(w.Person!.Value, w.x.Task.EstimateHours ?? 0m, w.x.Start, w.x.End, w.x.Task.Deadline));
        return CapacityMath.Build(people.Select(p => (p.Id, p.Name)), id => CalendarOf(store, people.First(p => p.Id == id)), work, from, to);
    }

    /// <summary>Кто делает задачу — тем же правилом, что в API: выбранный человек, иначе цель назначения, если это человек.</summary>
    private static Guid? PersonOf(DemoStore store, TaskModel task) =>
        task.ResolvedPersonId ?? (store.Actors.FirstOrDefault(a => a.Id == task.AssignmentTargetId) is { Kind: "Person" } target ? target.Id : null);

    /// <summary>Календарь человека вместе с общими графиками его подкоманд и их родителей.</summary>
    private static CalendarDto CalendarOf(DemoStore store, ActorModel person)
    {
        var departments = store.Actors.Where(a => a.Kind == "Department").ToDictionary(a => a.Id, a => a.ParentId);
        var own = person.DepartmentIds.Append(person.ParentId ?? Guid.Empty).Where(departments.ContainsKey);
        var shared = CapacityMath.DepartmentsWithParents(own, id => departments.GetValueOrDefault(id) is { } parent && departments.ContainsKey(parent) ? parent : null);
        return CapacityMath.Combine(Calendar(store, person.Id), shared.Select(id => store.Calendars.FirstOrDefault(c => c.ActorId == id)).OfType<CalendarDto>());
    }

    /// <summary>Риски тем же расчётом, что в API, включая зависимости.</summary>
    private static RiskDto[] Risks(DemoStore store, Guid teamId, DateOnly today)
    {
        var actors = store.Actors.ToDictionary(a => a.Id);
        var tasks = OpenTasks(store, teamId).Select(x =>
        {
            var person = PersonOf(store, x.Task) is { } id ? actors.GetValueOrDefault(id) : null;
            var target = person is null && x.Task.AssignmentTargetId is { } targetId ? actors.GetValueOrDefault(targetId)?.Name : null;
            return new RiskTask(x.Task.Id, x.Task.ParentId, x.Task.Title, x.Task.EstimateHours, x.Start, x.End, x.Task.Deadline, person?.Id, person?.Name, target);
        }).ToArray();
        var dependencies = store.Dependencies.Select(d => new RiskDependency(d.PredecessorId, d.SuccessorId));
        return RiskMath.Assess(tasks, dependencies, id => CalendarOf(store, actors[id]), today);
    }

    /// <summary>
    /// Автобаланс спринта той же моделью, что на сервере, но без решателя OR-Tools: в браузере есть только жадный
    /// поиск, поэтому demo предлагает лишь бережный режим.
    /// </summary>
    private static BalanceProposalDto Balance(DemoStore store, Guid teamId, Guid sprintId, BalanceRequest request)
    {
        var mode = BalanceModes.Normalize(request.Mode);
        if (mode != BalanceModes.Gentle)
            throw new NotSupportedException("Demo: режимы «Сроки и связи» и «Максимум» требуют сервера — решатель OR-Tools работает только там.");
        var sprint = store.Sprints.FirstOrDefault(s => s.Id == sprintId) ?? throw new InvalidOperationException("Спринт не найден.");
        var people = store.Actors.Where(a => a.Kind == "Person").ToArray();
        var persons = people.Select(p => new BalancePersonInput(p.Id, p.Name, p.RoleIds, p.DepartmentIds)).ToArray();
        var tasks = OpenTasks(store, teamId).Select(x => new BalanceTaskInput(x.Task.Id, x.Task.Title, x.Task.Status, x.Task.ParentId,
            x.Task.EstimateHours, x.Start, x.End, x.Task.Deadline, x.Task.AssignmentTargetId, x.Task.ResolvedPersonId)).ToArray();
        var links = TeamDependencies(store, teamId).Select(d => new BalanceLinkInput(d.PredecessorId, d.SuccessorId, d.Kind, d.LagDays)).ToArray();
        var problem = BalanceMath.Build(mode, sprintId, sprint.StartDate, sprint.EndDate, persons, id => CalendarOf(store, people.First(p => p.Id == id)), tasks, links);
        return BalanceMath.Proposal(problem, BalanceMath.Greedy(problem), BalanceMath.Heuristic, "Demo: простой поиск без решателя OR-Tools.");
    }

    public async Task<T> Save<T>(HttpMethod method, string path, object value)
    {
        var store = await Load();
        var s = SplitQuery(path).Resource.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Автобаланс только считает: хранилище не меняется.
        if (s is ["api", "teams", var bt, "sprints", var bs, "balance"]) return (T)(object)Balance(store, Id(bt), Id(bs), Read<BalanceRequest>(value));
        object saved = s switch
        {
            ["api", "teams", var t, "tasks"] => SaveTask(store, Id(t), null, value),
            ["api", "teams", _, "tasks", var id] => SaveTask(store, Guid.Empty, Id(id), value),
            ["api", "teams", _, "tasks", _, "dependencies"] => AddDependency(store, value),
            ["api", "teams", var t, "sprints"] => SaveSprint(store, Id(t), null, value),
            ["api", "teams", _, "sprints", var id] => SaveSprint(store, Guid.Empty, Id(id), value),
            _ => throw new NotSupportedException($"Demo: {method} {path} не поддерживается.")
        };
        await Persist();
        Changed?.Invoke();
        return (T)saved;
    }

    public async Task Send(HttpMethod method, string path, object value)
    {
        var store = await Load();
        var s = SplitQuery(path).Resource.Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (s)
        {
            case ["api", "auth", "profile"]:
                _displayName = Read<ProfileRequest>(value).DisplayName;
                break;
            case ["api", "workspaces"]:
                store.Workspaces.Add(new WorkspaceDto(Guid.NewGuid(), Read<NameRequest>(value).Name, false));
                break;
            case ["api", "workspaces", var w]:
                var settings = Read<WorkspaceSettingsRequest>(value);
                store.Workspaces = [.. store.Workspaces.Select(x => x.Id == Id(w) ? x with { Name = settings.Name, EnforcePermissions = settings.EnforcePermissions } : x)];
                break;
            case ["api", "workspaces", var w, "teams"]:
                store.Teams.Add(new TeamDto(Guid.NewGuid(), Id(w), Read<NameRequest>(value).Name, null));
                break;
            case ["api", "workspaces", _, "members"]:
                var membership = Read<MembershipRequest>(value);
                store.Members.Add(new MemberDto(Guid.NewGuid(), membership.Email, membership.Email.Split('@')[0], membership.IsOwner, membership.TeamIds));
                break;
            case ["api", "teams", var t, "projects"]:
                AddProject(store, Id(t), value);
                break;
            case ["api", "teams", _, "projects", var id]:
                if (method == HttpMethod.Delete) store.Projects.RemoveAll(p => p.Id == Id(id));
                else UpdateProject(store, Id(id), value);
                break;
            case ["api", "teams", _, "actors"]:
                AddActor(store, value);
                break;
            case ["api", "teams", _, "actors", var id]:
                if (method == HttpMethod.Delete) { store.Actors.RemoveAll(a => a.Id == Id(id)); store.Calendars.RemoveAll(c => c.ActorId == Id(id)); }
                else UpdateActor(store, Id(id), value);
                break;
            case ["api", "teams", _, "tasks", "dependencies", var dependency]:
                store.Dependencies.RemoveAll(d => d.Id == Id(dependency));
                break;
            case ["api", "teams", _, "tasks", _, "dependencies"]:
                AddDependency(store, value);
                break;
            case ["api", "teams", _, "tasks", var id]:
                if (method == HttpMethod.Delete) { store.Tasks.RemoveAll(x => x.Id == Id(id)); store.Dependencies.RemoveAll(d => d.PredecessorId == Id(id) || d.SuccessorId == Id(id)); }
                else SaveTask(store, Guid.Empty, Id(id), value);
                break;
            case ["api", "teams", _, "tasks", var id, "assignment"]:
                AssignTask(store, Id(id), method, value);
                break;
            case ["api", "teams", _, "actors", var a, "calendar"]:
                SaveCalendar(store, Id(a), value);
                break;
            case ["api", "teams", _, "actors", var a, "calendar", "exceptions"]:
                AddException(store, Id(a), value);
                break;
            case ["api", "teams", _, "actors", var a, "calendar", "exceptions", var ex]:
                RemoveException(store, Id(a), Id(ex));
                break;
            case ["api", "teams", _, "actors", var a, "calendar", "blocks"]:
                AddBlock(store, Id(a), value);
                break;
            case ["api", "teams", _, "actors", var a, "calendar", "blocks", var block]:
                RemoveBlock(store, Id(a), Id(block));
                break;
            default:
                throw new NotSupportedException($"Demo: {method} {path} не поддерживается.");
        }
        await Persist();
        Changed?.Invoke();
    }

    private static void AddProject(DemoStore store, Guid teamId, object value)
    {
        var request = Read<ProjectRequest>(value);
        var teams = request.TeamIds.Length > 0 ? request.TeamIds : [teamId];
        store.Projects.Add(new ProjectModel { Id = Guid.NewGuid(), Name = request.Name, Description = request.Description, TeamIds = teams });
    }

    private static void UpdateProject(DemoStore store, Guid id, object value)
    {
        var request = Read<ProjectRequest>(value);
        var project = store.Projects.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Проект не найден.");
        project.Name = request.Name;
        project.Description = request.Description;
        project.TeamIds = request.TeamIds;
    }

    private static void AddActor(DemoStore store, object value)
    {
        var request = Read<ActorRequest>(value);
        var actor = new ActorModel
        {
            Id = Guid.NewGuid(), Name = request.Name, Kind = request.Kind, Email = request.Email,
            ParentId = request.ParentId, UserId = request.UserId, RoleIds = request.RoleIds ?? [], DepartmentIds = request.DepartmentIds ?? []
        };
        store.Actors.Add(actor);
        if (actor.Kind == "Person") store.Calendars.Add(new CalendarDto(actor.Id, "Europe/Moscow", 8m, 8m, 8m, 8m, 8m, 0m, 0m, [], []));
    }

    private static void UpdateActor(DemoStore store, Guid id, object value)
    {
        var request = Read<ActorRequest>(value);
        var actor = store.Actors.FirstOrDefault(a => a.Id == id) ?? throw new InvalidOperationException("Участник не найден.");
        actor.Name = request.Name;
        actor.Kind = request.Kind;
        actor.Email = request.Email;
        actor.ParentId = request.ParentId;
        actor.UserId = request.UserId;
        actor.RoleIds = request.RoleIds ?? [];
        actor.DepartmentIds = request.DepartmentIds ?? [];
    }

    /// <summary>Родитель охватывает детей — то же правило, что в API; версия предков не меняется.</summary>
    private static void KeepParentsAroundChildren(DemoStore store, TaskModel task)
    {
        var dates = store.Tasks.Where(t => t.TeamId == task.TeamId).Select(t => new TaskDates(t.Id, t.ParentId, t.StartDate, t.EndDate)).ToArray();
        foreach (var update in TaskWindows.Enforce(dates, task.Id))
        {
            var entity = store.Tasks.First(t => t.Id == update.Id);
            entity.StartDate = update.Start;
            entity.EndDate = update.End;
            if (entity != task) AddHistory(store, entity.Id, "Сроки расширены под подзадачи");
        }
    }

    private static TaskModel SaveTask(DemoStore store, Guid teamId, Guid? id, object value)
    {
        var request = Read<TaskRequest>(value);
        if (id is not { } taskId)
        {
            var created = new TaskModel
            {
                Id = Guid.NewGuid(), TeamId = teamId, ProjectId = request.ProjectId, ParentId = request.ParentId,
                Title = request.Title, Description = request.Description, Status = request.Status, EstimateHours = request.EstimateHours,
                Deadline = request.Deadline, StartDate = request.StartDate, EndDate = request.EndDate, Order = request.Order, Version = 1
            };
            store.Tasks.Add(created);
            AddHistory(store, created.Id, "Задача создана");
            KeepParentsAroundChildren(store, created);
            return WithSprints(store, [created])[0];
        }
        var task = store.Tasks.FirstOrDefault(t => t.Id == taskId) ?? throw new InvalidOperationException("Задача не найдена.");
        task.ProjectId = request.ProjectId;
        task.ParentId = request.ParentId;
        task.Title = request.Title;
        task.Description = request.Description;
        task.Status = request.Status;
        task.EstimateHours = request.EstimateHours;
        task.Deadline = request.Deadline;
        task.StartDate = request.StartDate;
        task.EndDate = request.EndDate;
        task.Order = request.Order;
        task.Version++;
        AddHistory(store, task.Id, $"Изменена (статус {Labels.Status(task.Status)})");
        KeepParentsAroundChildren(store, task);
        return WithSprints(store, [task])[0];
    }

    private static SprintModel SaveSprint(DemoStore store, Guid teamId, Guid? id, object value)
    {
        var request = Read<SprintRequest>(value);
        if (id is not { } sprintId)
        {
            var created = new SprintModel { Id = Guid.NewGuid(), Name = request.Name, Goal = request.Goal, StartDate = request.StartDate, EndDate = request.EndDate, Status = request.Status };
            store.Sprints.Add(created);
            store.SprintTeam[created.Id] = teamId;
            return created;
        }
        var sprint = store.Sprints.FirstOrDefault(s => s.Id == sprintId) ?? throw new InvalidOperationException("Спринт не найден.");
        sprint.Name = request.Name;
        sprint.Goal = request.Goal;
        sprint.StartDate = request.StartDate;
        sprint.EndDate = request.EndDate;
        sprint.Status = request.Status;
        return sprint;
    }

    private static void AssignTask(DemoStore store, Guid taskId, HttpMethod method, object value)
    {
        var task = store.Tasks.FirstOrDefault(t => t.Id == taskId) ?? throw new InvalidOperationException("Задача не найдена.");
        if (method == HttpMethod.Delete)
        {
            task.AssignmentTargetId = null;
            task.ResolvedPersonId = null;
            AddHistory(store, task.Id, "Назначение снято");
        }
        else
        {
            var request = Read<AssignmentRequest>(value);
            task.AssignmentTargetId = request.TargetId;
            task.ResolvedPersonId = request.ResolvedPersonId;
            AddHistory(store, task.Id, "Назначен исполнитель");
        }
        task.Version++;
    }

    private static void SaveCalendar(DemoStore store, Guid actorId, object value)
    {
        var request = Read<CalendarRequest>(value);
        var existing = store.Calendars.FirstOrDefault(c => c.ActorId == actorId);
        var updated = new CalendarDto(actorId, request.TimeZone, request.Monday, request.Tuesday, request.Wednesday, request.Thursday,
            request.Friday, request.Saturday, request.Sunday, existing?.Exceptions ?? [], existing?.RecurringBlocks ?? []);
        store.Calendars.RemoveAll(c => c.ActorId == actorId);
        store.Calendars.Add(updated);
    }

    private static void AddException(DemoStore store, Guid actorId, object value)
    {
        var request = Read<CalendarExceptionRequest>(value);
        var calendar = Calendar(store, actorId);
        var exceptions = calendar.Exceptions.Append(new CalendarExceptionDto(Guid.NewGuid(), request.From, request.To, request.AvailableHours, request.Reason)).ToArray();
        Replace(store, calendar with { Exceptions = exceptions });
    }

    private static void RemoveException(DemoStore store, Guid actorId, Guid exceptionId)
    {
        var calendar = Calendar(store, actorId);
        Replace(store, calendar with { Exceptions = [.. calendar.Exceptions.Where(e => e.Id != exceptionId)] });
    }

    private static void AddBlock(DemoStore store, Guid actorId, object value)
    {
        var request = Read<RecurringBlockRequest>(value);
        var calendar = Calendar(store, actorId);
        var blocks = calendar.RecurringBlocks.Append(new RecurringBlockDto(Guid.NewGuid(), request.DayOfWeek, request.Hours, request.Name)).ToArray();
        Replace(store, calendar with { RecurringBlocks = blocks });
    }

    private static void RemoveBlock(DemoStore store, Guid actorId, Guid blockId)
    {
        var calendar = Calendar(store, actorId);
        Replace(store, calendar with { RecurringBlocks = [.. calendar.RecurringBlocks.Where(b => b.Id != blockId)] });
    }

    private static void Replace(DemoStore store, CalendarDto calendar)
    {
        store.Calendars.RemoveAll(c => c.ActorId == calendar.ActorId);
        store.Calendars.Add(calendar);
    }

    private static void AddHistory(DemoStore store, Guid taskId, string summary)
    {
        if (!store.History.TryGetValue(taskId, out var entries)) store.History[taskId] = entries = [];
        entries.Insert(0, new HistoryDto(Guid.NewGuid(), DateTimeOffset.Now, DemoIds.User, summary));
    }

    private async Task<DemoStore> Load()
    {
        if (_store is not null) return _store;
        var raw = await js.InvokeAsync<string?>("localStorage.getItem", DataKey);
        _store = string.IsNullOrWhiteSpace(raw) ? DemoSeed.Create() : JsonSerializer.Deserialize<DemoStore>(DatesFromSprints(raw), Json) ?? DemoSeed.Create();
        return _store;
    }

    /// <summary>
    /// Данные демо, сохранённые до того, как спринт стал следствием дат: задаче со спринтом недостающие даты ставятся
    /// по окну спринта (как в миграции API), чтобы она не выпала из плана.
    /// </summary>
    private static string DatesFromSprints(string raw)
    {
        if (!raw.Contains("\"sprintId\"", StringComparison.Ordinal) || System.Text.Json.Nodes.JsonNode.Parse(raw) is not System.Text.Json.Nodes.JsonObject root) return raw;
        var sprints = (root["sprints"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>()
            .Where(s => s["id"] is not null).ToDictionary(s => s["id"]!.GetValue<string>());
        foreach (var task in (root["tasks"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>())
        {
            if (task["sprintId"]?.GetValue<string>() is { } sprintId && sprints.TryGetValue(sprintId, out var sprint))
            {
                task["startDate"] ??= task["endDate"]?.DeepClone() is { } end && string.CompareOrdinal(end.GetValue<string>(), sprint["startDate"]!.GetValue<string>()) < 0 ? end : sprint["startDate"]!.DeepClone();
                task["endDate"] ??= string.CompareOrdinal(task["startDate"]!.GetValue<string>(), sprint["endDate"]!.GetValue<string>()) > 0 ? task["startDate"]!.DeepClone() : sprint["endDate"]!.DeepClone();
            }
            task.Remove("sprintId");
        }
        return root.ToJsonString();
    }

    private async Task Persist() => await js.InvokeVoidAsync("localStorage.setItem", DataKey, JsonSerializer.Serialize(_store, Json));

    private static string? ReadString(object value, string property)
    {
        var element = JsonSerializer.SerializeToElement(value, Json);
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }

    private static TRequest Read<TRequest>(object value) where TRequest : notnull =>
        JsonSerializer.Deserialize<TRequest>(JsonSerializer.SerializeToElement(value, Json), Json)!;

    private static Guid Id(string value) => Guid.Parse(value);
}

/// <summary>Стабильные идентификаторы демо-набора: ссылки между записями не зависят от порядка создания.</summary>
internal static class DemoIds
{
    public static readonly Guid User = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Workspace = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Team = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid GrowthTeam = new("33333333-3333-3333-3333-333333333344");
    public static readonly Guid Project = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid MobileProject = new("44444444-4444-4444-4444-444444444455");
    public static readonly Guid Department = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid BackendRole = new("66666666-6666-6666-6666-666666666666");
    public static readonly Guid FrontendRole = new("66666666-6666-6666-6666-666666666677");
    public static readonly Guid Anna = new("77777777-7777-7777-7777-777777777777");
    public static readonly Guid Petr = new("77777777-7777-7777-7777-777777777788");
    public static readonly Guid Olga = new("77777777-7777-7777-7777-777777777799");
    public static readonly Guid Sprint1 = new("88888888-8888-8888-8888-888888888888");
    public static readonly Guid Sprint2 = new("88888888-8888-8888-8888-888888888899");
    public static readonly Guid ParentTask = new("99999999-9999-9999-9999-999999999999");
    public static readonly Guid ApiTask = new("99999999-9999-9999-9999-999999999991");
    public static readonly Guid UiTask = new("99999999-9999-9999-9999-999999999992");
    public static readonly Guid LateTask = new("99999999-9999-9999-9999-999999999993");
}

/// <summary>Демо-хранилище: то, что лежит в localStorage под ключом <c>alva-demo-data</c>.</summary>
public sealed class DemoStore
{
    public List<WorkspaceDto> Workspaces { get; set; } = [];
    public List<TeamDto> Teams { get; set; } = [];
    public List<MemberDto> Members { get; set; } = [];
    public List<ProjectModel> Projects { get; set; } = [];
    public List<ActorModel> Actors { get; set; } = [];
    public List<SprintModel> Sprints { get; set; } = [];
    public List<TaskModel> Tasks { get; set; } = [];
    public List<CalendarDto> Calendars { get; set; } = [];
    public List<DependencyDto> Dependencies { get; set; } = [];
    public Dictionary<Guid, Guid> SprintTeam { get; set; } = [];
    public Dictionary<Guid, List<HistoryDto>> History { get; set; } = [];
}

/// <summary>Начальный демо-набор: площадка, две команды, участники, календари, проекты, спринты и задачи.</summary>
internal static class DemoSeed
{
    public static DemoStore Create()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var week = new CalendarDto(DemoIds.Anna, "Europe/Moscow", 8m, 8m, 8m, 8m, 8m, 0m, 0m, [], []);
        var now = DateTimeOffset.Now;
        var store = new DemoStore
        {
            Workspaces = [new WorkspaceDto(DemoIds.Workspace, "Демо-площадка", false)],
            Teams =
            [
                new TeamDto(DemoIds.Team, DemoIds.Workspace, "Продуктовая команда", DemoIds.Project),
                new TeamDto(DemoIds.GrowthTeam, DemoIds.Workspace, "Команда роста", null)
            ],
            Members = [new MemberDto(DemoIds.User, "demo@alva.local", "Демо-пользователь", true, [DemoIds.Team, DemoIds.GrowthTeam])],
            Projects =
            [
                new ProjectModel { Id = DemoIds.Project, Name = "Платформа Alva", Description = "Ядро планирования", TeamIds = [DemoIds.Team] },
                new ProjectModel { Id = DemoIds.MobileProject, Name = "Мобильный клиент", Description = "PWA для планшета", TeamIds = [DemoIds.Team] }
            ],
            Actors =
            [
                new ActorModel { Id = DemoIds.Department, Name = "Разработка", Kind = "Department" },
                new ActorModel { Id = DemoIds.BackendRole, Name = "Backend", Kind = "Role", DepartmentIds = [DemoIds.Department] },
                new ActorModel { Id = DemoIds.FrontendRole, Name = "Frontend", Kind = "Role", DepartmentIds = [DemoIds.Department] },
                new ActorModel { Id = DemoIds.Anna, Name = "Анна Ковалёва", Kind = "Person", Email = "anna@alva.local", UserId = DemoIds.User, ParentId = DemoIds.Department, RoleIds = [DemoIds.BackendRole], DepartmentIds = [DemoIds.Department] },
                new ActorModel { Id = DemoIds.Petr, Name = "Пётр Смирнов", Kind = "Person", Email = "petr@alva.local", ParentId = DemoIds.Department, RoleIds = [DemoIds.FrontendRole], DepartmentIds = [DemoIds.Department] },
                new ActorModel { Id = DemoIds.Olga, Name = "Ольга Демидова", Kind = "Person", Email = "olga@alva.local", ParentId = DemoIds.Department, RoleIds = [DemoIds.BackendRole], DepartmentIds = [DemoIds.Department] }
            ],
            Calendars =
            [
                week,
                week with { ActorId = DemoIds.Petr },
                week with { ActorId = DemoIds.Olga }
            ],
            Sprints =
            [
                new SprintModel { Id = DemoIds.Sprint1, Name = "Спринт 1", Goal = "Планирование задач", StartDate = today.AddDays(-7), EndDate = today.AddDays(6), Status = "Active" },
                new SprintModel { Id = DemoIds.Sprint2, Name = "Спринт 2", Goal = "Отчётность", StartDate = today.AddDays(7), EndDate = today.AddDays(20), Status = "Planned" }
            ],
            SprintTeam = { [DemoIds.Sprint1] = DemoIds.Team, [DemoIds.Sprint2] = DemoIds.Team },
            Dependencies = [new DependencyDto(new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), DemoIds.ApiTask, DemoIds.UiTask, "FS", 0)],
            Tasks =
            [
                new TaskModel { Id = DemoIds.ParentTask, TeamId = DemoIds.Team, ProjectId = DemoIds.Project, Title = "Планирование ресурсов", Description = "Иерархия работ и сроки", Status = "InProgress", StartDate = today.AddDays(-7), EndDate = today.AddDays(6), Deadline = today.AddDays(8), Order = 1, Version = 1 },
                new TaskModel { Id = DemoIds.ApiTask, TeamId = DemoIds.Team, ProjectId = DemoIds.Project, ParentId = DemoIds.ParentTask, Title = "API планирования", Description = "Проекты, задачи, спринты", Status = "InProgress", EstimateHours = 16m, StartDate = today.AddDays(-7), EndDate = today.AddDays(1), Deadline = today.AddDays(3), Order = 1, AssignmentTargetId = DemoIds.BackendRole, ResolvedPersonId = DemoIds.Anna, Version = 1 },
                new TaskModel { Id = DemoIds.UiTask, TeamId = DemoIds.Team, ProjectId = DemoIds.MobileProject, ParentId = DemoIds.ParentTask, Title = "Экраны планирования", Description = "Задачи и спринты на планшете", Status = "Planned", EstimateHours = 10m, StartDate = today.AddDays(-2), EndDate = today.AddDays(4), Deadline = today.AddDays(6), Order = 2, AssignmentTargetId = DemoIds.FrontendRole, ResolvedPersonId = DemoIds.Petr, Version = 1 },
                new TaskModel { Id = DemoIds.LateTask, TeamId = DemoIds.Team, ProjectId = DemoIds.Project, Title = "Отчёт по загрузке", Description = "Риск: срок уже прошёл", Status = "InProgress", EstimateHours = 8m, StartDate = today.AddDays(-5), EndDate = today.AddDays(3), Deadline = today.AddDays(-2), Order = 3, AssignmentTargetId = DemoIds.BackendRole, ResolvedPersonId = DemoIds.Olga, Version = 1 }
            ],
            History =
            {
                [DemoIds.ParentTask] = [new HistoryDto(Guid.NewGuid(), now.AddDays(-7), DemoIds.User, "Задача создана")],
                [DemoIds.ApiTask] = [new HistoryDto(Guid.NewGuid(), now.AddDays(-7), DemoIds.User, "Задача создана"), new HistoryDto(Guid.NewGuid(), now.AddDays(-2), DemoIds.User, "Назначен исполнитель")],
                [DemoIds.UiTask] = [new HistoryDto(Guid.NewGuid(), now.AddDays(-2), DemoIds.User, "Задача создана")],
                [DemoIds.LateTask] = [new HistoryDto(Guid.NewGuid(), now.AddDays(-5), DemoIds.User, "Задача создана")]
            }
        };
        return store;
    }
}
