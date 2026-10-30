using System.Collections.Concurrent;
using System.Text.Json;
using Alva.Domain;
using Alva.Infrastructure;
using Alva.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Alva.Api.Integrations;

/// <summary>Итог прогона: что сделано и, если не вышло, почему.</summary>
public sealed record IntegrationSyncResult(TaskSourceRun Run);

/// <summary>
/// Синхронизация задач из модуля интеграции. Идемпотентна: задача привязывается к внешнему ключу, и повтор её обновляет.
/// Внешняя система — источник названия, статуса, оценок и сроков; планирование (даты, назначение, связи) Alva не отдаёт:
/// даты переносятся только целиком и не трогают зафиксированные, назначение ставится, только если его нет.
/// </summary>
public sealed class IntegrationSync(AlvaDbContext db, TaskSourceModules modules, IDataProtectionProvider protection, ILogger<IntegrationSync> log)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Running = new();
    private const string Purpose = "Alva.Integrations.Secrets.v1";

    public IDataProtector Protector => protection.CreateProtector(Purpose);

    /// <summary>Подключение в словах модуля: настройки и расшифрованные секреты.</summary>
    public IntegrationConnection Describe(TaskSourceConnection connection) =>
        new(connection.Id, connection.Name, Settings(connection), Secrets(connection));

    public static Dictionary<string, string> Settings(TaskSourceConnection connection) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(connection.SettingsJson) ?? [];

    public Dictionary<string, string> Secrets(TaskSourceConnection connection) =>
        connection.ProtectedSecrets.Length == 0 ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(Protector.Unprotect(connection.ProtectedSecrets)) ?? [];

    public string Protect(Dictionary<string, string> secrets) => secrets.Count == 0 ? "" : Protector.Protect(JsonSerializer.Serialize(secrets));

    public ITaskSourceModule Module(TaskSourceConnection connection) =>
        modules.Find(connection.Module) ?? throw new IntegrationException($"Модуль «{connection.Module}» не установлен на этом сервере.");

    public async Task<IntegrationCheck> CheckAsync(TaskSourceConnection connection, CancellationToken ct)
    {
        var source = await Module(connection).ConnectAsync(Describe(connection), ct);
        return await source.CheckAsync(ct);
    }

    /// <summary>
    /// Прогон синхронизации. Одновременно по подключению идёт один прогон; второй получает 409. Ошибка модуля не бросается
    /// наружу, а становится неуспешным прогоном в журнале — курсор при этом не сдвигается.
    /// </summary>
    public async Task<TaskSourceRun> RunAsync(TaskSourceConnection connection, bool full, CancellationToken ct)
    {
        var gate = Running.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) throw new ApiException(409, "Синхронизация этого подключения уже идёт.");
        try
        {
            var run = new TaskSourceRun { ConnectionId = connection.Id, StartedAt = DateTimeOffset.UtcNow };
            try
            {
                await Sync(connection, full, run, ct);
                run.Ok = true;
                connection.SyncedThrough = run.StartedAt;
            }
            catch (Exception e) when (e is IntegrationException or ApiException or DbUpdateException)
            {
                db.ChangeTracker.Clear();
                run.Ok = false;
                run.Message = e is DbUpdateException ? "Не удалось сохранить задачи: данные изменились во время синхронизации. Повторите её." : e.Message;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Синхронизация подключения {Connection} упала", connection.Id);
                db.ChangeTracker.Clear();
                run.Ok = false;
                run.Message = "Синхронизация прервалась из-за внутренней ошибки; подробности — в журнале сервера.";
            }
            run.FinishedAt = DateTimeOffset.UtcNow;
            if (db.Entry(connection).State == EntityState.Detached) db.Attach(connection);
            connection.LastRunAt = run.StartedAt;
            connection.LastOk = run.Ok;
            connection.LastMessage = run.Message;
            if (!run.Ok) db.Entry(connection).Property(x => x.SyncedThrough).IsModified = false;
            db.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
            return run;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task Sync(TaskSourceConnection connection, bool full, TaskSourceRun run, CancellationToken ct)
    {
        var source = await Module(connection).ConnectAsync(Describe(connection), ct);
        var external = new List<ExternalTask>();
        await foreach (var item in source.ReadTasksAsync(full ? null : connection.SyncedThrough, ct)) external.Add(item);
        // Повтор ключа в одной выдаче (страницы сдвинулись во время чтения) — берётся последний.
        external = external.GroupBy(x => x.Key, StringComparer.Ordinal).Select(g => g.Last()).ToList();

        var teamId = connection.TeamId;
        var userId = connection.CreatedBy;
        var links = await db.ExternalTaskLinks.Where(x => x.ConnectionId == connection.Id).ToDictionaryAsync(x => x.ExternalKey, StringComparer.Ordinal, ct);
        var linkedIds = links.Values.Select(x => x.TaskId).ToArray();
        var tasks = await db.Tasks.Where(x => linkedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var notes = new List<string>();

        foreach (var item in external)
        {
            if (links.TryGetValue(item.Key, out var link) && tasks.TryGetValue(link.TaskId, out var task))
            {
                link.ExternalUrl = item.Url ?? link.ExternalUrl;
                link.ExternalUpdatedAt = item.UpdatedAt ?? link.ExternalUpdatedAt;
                if (task.Archived || task.TeamId != teamId) { run.Skipped++; continue; }
                var changes = Apply(task, item);
                if (changes.Count == 0) { run.Unchanged++; continue; }
                task.Version++;
                db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = Trim($"Обновлено из «{connection.Name}»: {string.Join(", ", changes)}") });
                run.Updated++;
            }
            else
            {
                task = new TaskCard { TeamId = teamId, Title = "" };
                Apply(task, item);
                var order = await db.Tasks.Where(x => x.TeamId == teamId && x.ParentId == null).Select(x => (int?)x.Order).MaxAsync(ct) ?? 0;
                task.Order = order + 1 + run.Created;
                db.Add(task);
                tasks[task.Id] = task;
                links[item.Key] = new ExternalTaskLink { ConnectionId = connection.Id, TaskId = task.Id, ExternalKey = item.Key, ExternalUrl = item.Url, ExternalUpdatedAt = item.UpdatedAt };
                db.Add(links[item.Key]);
                db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = Trim($"Создано из «{connection.Name}» ({item.Key})") });
                run.Created++;
            }
        }
        await db.SaveChangesAsync(ct);

        TaskCard? Linked(string? key) => key is not null && links.TryGetValue(key, out var l) && tasks.TryGetValue(l.TaskId, out var t) && !t.Archived ? t : null;

        // Родители — после сохранения: правило «родитель охватывает детей» читает задачи из базы. Цепочка предков
        // проверяется по всей команде, не только по пришедшим задачам: иначе цикл мог бы пройти через задачу Alva.
        var parentOf = await db.Tasks.Where(x => x.TeamId == teamId).ToDictionaryAsync(x => x.Id, x => x.ParentId, ct);
        foreach (var item in external)
        {
            if (Linked(item.Key) is not { } task || Linked(item.ParentKey) is not { } parent || task.ParentId == parent.Id) continue;
            if (Ancestors(parent.Id, parentOf).Contains(task.Id)) { notes.Add($"{item.Key}: родитель {item.ParentKey} дал бы цикл — пропущен."); continue; }
            var before = task.ParentId;
            task.ParentId = parent.Id;
            try
            {
                await TaskEndpoints.KeepParentsAroundChildren(db, teamId, task, userId);
                parentOf[task.Id] = parent.Id;
            }
            catch (ApiException e) { task.ParentId = before; notes.Add($"{item.Key}: {e.Message}"); }
        }
        // Источник мог снова дать родителю свои даты, уже узкие для детей: родитель опять охватывает их, как после ручной правки.
        var withChildren = external.Select(x => Linked(x.Key)).OfType<TaskCard>().Where(t => t.ParentId is not null)
            .Select(t => t.ParentId!.Value).Distinct().Select(id => tasks.GetValueOrDefault(id)).OfType<TaskCard>().ToList();
        foreach (var parent in withChildren)
        {
            try { await TaskEndpoints.KeepParentsAroundChildren(db, teamId, parent, userId); }
            catch (ApiException e) { notes.Add(e.Message); }
        }

        var taskIds = tasks.Keys.ToArray();
        var dependencies = await db.Dependencies.Where(x => taskIds.Contains(x.PredecessorId) || taskIds.Contains(x.SuccessorId))
            .Select(x => new { x.PredecessorId, x.SuccessorId }).ToListAsync(ct);
        var pairs = dependencies.Select(x => (x.PredecessorId, x.SuccessorId)).ToHashSet();
        foreach (var link in external.SelectMany(x => x.Links ?? []).Distinct())
        {
            if (Linked(link.PredecessorKey) is not { } from || Linked(link.SuccessorKey) is not { } to || from.Id == to.Id) continue;
            if (pairs.Contains((from.Id, to.Id)) || pairs.Contains((to.Id, from.Id))) continue;
            if (!Enum.TryParse<DependencyKind>(link.Kind, true, out var kind)) kind = DependencyKind.FS;
            db.Add(new TaskDependency { PredecessorId = from.Id, SuccessorId = to.Id, Kind = kind, LagDays = Math.Clamp(link.LagDays, -3650, 3650) });
            pairs.Add((from.Id, to.Id));
        }

        // Исполнитель — по email участника; только для листовой задачи и только если назначения нет: назначение планирует Alva.
        var people = (await db.Actors.Where(x => x.TeamId == teamId && x.Kind == ActorKind.Person && !x.Archived && x.Email != null).ToListAsync(ct))
            .GroupBy(x => x.Email!.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var assigned = (await db.Assignments.Where(x => taskIds.Contains(x.TaskId)).Select(x => x.TaskId).ToListAsync(ct)).ToHashSet();
        // Родители — по карте команды вместе с только что поставленными: они ещё не сохранены, и запрос к базе их не увидел бы.
        var parents = parentOf.Values.OfType<Guid>().ToHashSet();
        foreach (var item in external)
        {
            if (Linked(item.Key) is not { } task || item.AssigneeEmail is not { Length: > 0 } email || assigned.Contains(task.Id) || parents.Contains(task.Id)) continue;
            if (!people.TryGetValue(email.Trim(), out var person)) continue;
            db.Add(new Assignment { TaskId = task.Id, TargetId = person.Id });
            db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = Trim($"Назначено по «{connection.Name}»: {person.Name}") });
            assigned.Add(task.Id);
        }
        await db.SaveChangesAsync(ct);

        run.Message = Trim(string.Join(" ", new[] { $"Получено задач: {external.Count}." }.Concat(notes)));
    }

    /// <summary>Переносит в задачу то, что источник знает; возвращает названия изменённых полей для истории.</summary>
    internal static List<string> Apply(TaskCard task, ExternalTask item)
    {
        var changes = new List<string>();
        void Set<T>(T current, T value, Action<T> assign, string label)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            assign(value);
            changes.Add(label);
        }
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Key : item.Title.Trim();
        Set(task.Title, title.Length > 500 ? title[..500] : title, v => task.Title = v, "название");
        if (item.Description is { } description) Set(task.Description, description.Length > 10000 ? description[..10000] : description, v => task.Description = v, "описание");
        Set(task.Status, Enum.Parse<WorkStatus>(item.Status.ToString()), v => task.Status = v, item.StatusName is { Length: > 0 } name ? $"статус ({name})" : "статус");
        if (item.Priority is { } priority) Set(task.Priority, Enum.Parse<TaskPriority>(priority.ToString()), v => task.Priority = v, "приоритет");
        if (item.EstimateHours is { } estimate && estimate >= 0) Set(task.EstimateHours, (decimal?)Math.Round(estimate, 2), v => task.EstimateHours = v, "оценка");
        if (item.RemainingHours is { } remaining && remaining >= 0) Set(task.RemainingHours, (decimal?)Math.Round(remaining, 2), v => task.RemainingHours = v, "остаток");
        if (item.Deadline is { } deadline) Set(task.Deadline, (DateOnly?)deadline, v => task.Deadline = v, "срок");
        // Даты — только парой и не поверх фиксации: половина окна или перенос зафиксированной задачи сломали бы план.
        if (item is { Start: { } start, End: { } end } && start <= end && !task.DatesFixed && (task.StartDate != start || task.EndDate != end))
        {
            task.StartDate = start;
            task.EndDate = end;
            changes.Add("даты");
        }
        return changes;
    }

    /// <summary>Задача и все её предки по карте родителей команды.</summary>
    private static HashSet<Guid> Ancestors(Guid taskId, Dictionary<Guid, Guid?> parentOf)
    {
        var seen = new HashSet<Guid> { taskId };
        for (var current = parentOf.GetValueOrDefault(taskId); current is { } id && seen.Add(id);)
            current = parentOf.GetValueOrDefault(id);
        return seen;
    }

    private static string Trim(string text) => text.Length > 500 ? text[..500] : text;
}
