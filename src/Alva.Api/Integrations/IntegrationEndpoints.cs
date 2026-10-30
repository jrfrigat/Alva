using System.Security.Claims;
using System.Text.Json;
using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Alva.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Alva.Api.Integrations;

/// <summary>
/// Подключения команды к источникам задач. Права — область «Данные»: смотреть — view, синхронизировать — edit,
/// настраивать — manage. Секреты в ответах не появляются никогда.
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrations(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}/integrations").RequireAuthorization();
        group.MapGet("/modules", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db, TaskSourceModules modules) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "view");
            return modules.All.OrderBy(m => m.Name).Select(Module).ToArray();
        });
        group.MapGet("", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db, TaskSourceModules modules) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "view");
            var connections = await db.TaskSources.AsNoTracking().Where(x => x.TeamId == teamId).OrderBy(x => x.Name).ToListAsync();
            var ids = connections.Select(x => x.Id).ToArray();
            var linked = await db.ExternalTaskLinks.Where(x => ids.Contains(x.ConnectionId)).GroupBy(x => x.ConnectionId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            return connections.Select(x => Dto(x, modules, linked.GetValueOrDefault(x.Id))).ToArray();
        });
        group.MapPost("", async (Guid teamId, IntegrationConnectionRequest request, ClaimsPrincipal user, AlvaDbContext db, TaskSourceModules modules, IntegrationSync sync) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "manage");
            var connection = new TaskSourceConnection { TeamId = teamId, CreatedBy = user.UserId() };
            await Apply(db, modules, sync, connection, request);
            db.Add(connection);
            await db.SaveChangesAsync();
            return Dto(connection, modules, 0);
        });
        group.MapPut("/{id:guid}", async (Guid teamId, Guid id, IntegrationConnectionRequest request, ClaimsPrincipal user, AlvaDbContext db, TaskSourceModules modules, IntegrationSync sync) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "manage");
            var connection = await Find(db, teamId, id);
            if (!string.Equals(request.Module, connection.Module, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(400, "Модуль подключения не меняется: создайте новое подключение.");
            await Apply(db, modules, sync, connection, request);
            await db.SaveChangesAsync();
            return Dto(connection, modules, await db.ExternalTaskLinks.CountAsync(x => x.ConnectionId == id));
        });
        group.MapDelete("/{id:guid}", async (Guid teamId, Guid id, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "manage");
            var connection = await Find(db, teamId, id);
            // Задачи остаются в Alva: снимаются только привязки и журнал.
            await db.ExternalTaskLinks.Where(x => x.ConnectionId == id).ExecuteDeleteAsync();
            await db.TaskSourceRuns.Where(x => x.ConnectionId == id).ExecuteDeleteAsync();
            db.Remove(connection);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        group.MapPost("/{id:guid}/check", async (Guid teamId, Guid id, ClaimsPrincipal user, AlvaDbContext db, IntegrationSync sync, CancellationToken ct) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "edit");
            var connection = await Find(db, teamId, id);
            try
            {
                var check = await sync.CheckAsync(connection, ct);
                return new IntegrationCheckDto(check.Ok, check.Message, check.Account);
            }
            catch (IntegrationException e) { return new IntegrationCheckDto(false, e.Message, null); }
        });
        group.MapPost("/{id:guid}/sync", async (Guid teamId, Guid id, bool? full, ClaimsPrincipal user, AlvaDbContext db, IntegrationSync sync, CancellationToken ct) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "edit");
            var connection = await Find(db, teamId, id);
            return Run(await sync.RunAsync(connection, full == true, ct));
        });
        group.MapGet("/{id:guid}/runs", async (Guid teamId, Guid id, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "data", "view");
            await Find(db, teamId, id);
            var runs = await db.TaskSourceRuns.AsNoTracking().Where(x => x.ConnectionId == id).ToListAsync();
            return runs.OrderByDescending(x => x.StartedAt).Take(20).Select(Run).ToArray();
        });
    }

    private static async Task<TaskSourceConnection> Find(AlvaDbContext db, Guid teamId, Guid id) =>
        await db.TaskSources.SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == id) ?? throw new ApiException(404, "Подключение не найдено.");

    /// <summary>
    /// Переносит запрос в подключение: только ключи схемы модуля, секретные — в зашифрованные секреты. Пустой секрет
    /// оставляет сохранённый — форма не обязана знать его, чтобы поменять имя подключения.
    /// </summary>
    private static async Task Apply(AlvaDbContext db, TaskSourceModules modules, IntegrationSync sync, TaskSourceConnection connection, IntegrationConnectionRequest request)
    {
        var module = modules.Find(request.Module ?? "") ?? throw new ApiException(400, $"Модуль «{request.Module}» не установлен на этом сервере.");
        var name = AccessControl.RequiredName(request.Name);
        if (await db.TaskSources.AnyAsync(x => x.TeamId == connection.TeamId && x.Name == name && x.Id != connection.Id))
            throw new ApiException(409, "Подключение с таким названием уже есть.");
        if (request.AutoSyncMinutes is < 5 or > 1440) throw new ApiException(400, "Автосинхронизация — от 5 до 1440 минут или выключена.");
        var settings = new Dictionary<string, string>();
        var secrets = connection.ProtectedSecrets.Length == 0 ? [] : sync.Secrets(connection);
        foreach (var setting in module.Settings)
        {
            if (setting.Kind == IntegrationSettingKind.Secret)
            {
                if (request.Secrets?.GetValueOrDefault(setting.Key) is { Length: > 0 } secret && !string.IsNullOrWhiteSpace(secret)) secrets[setting.Key] = secret.Trim();
            }
            else if (request.Settings?.GetValueOrDefault(setting.Key) is { } value && !string.IsNullOrWhiteSpace(value)) settings[setting.Key] = value.Trim();
        }
        var errors = IntegrationSettings.Validate(module, new IntegrationConnection(connection.Id, name, settings, secrets));
        if (errors.Count > 0) throw new ApiException(400, string.Join(" ", errors.Select(e => e.Message)));
        connection.Module = module.Id;
        connection.Name = name;
        connection.SettingsJson = JsonSerializer.Serialize(settings);
        connection.ProtectedSecrets = sync.Protect(secrets);
        connection.Enabled = request.Enabled;
        connection.AutoSyncMinutes = request.AutoSyncMinutes;
    }

    private static IntegrationModuleDto Module(ITaskSourceModule module) => new(module.Id, module.Name, module.Description,
        Enum.GetValues<IntegrationCapabilities>().Where(c => c != IntegrationCapabilities.None && module.Capabilities.HasFlag(c)).Select(c => c.ToString()).ToArray(),
        module.Settings.Select(s => new IntegrationSettingDto(s.Key, s.Label, s.Kind.ToString(), s.Required, s.Default, s.Description,
            s.Choices?.Select(c => new IntegrationChoiceDto(c.Value, c.Label)).ToArray())).ToArray());

    private static IntegrationConnectionDto Dto(TaskSourceConnection x, TaskSourceModules modules, int linked)
    {
        var module = modules.Find(x.Module);
        // Какие секреты заданы — по схеме модуля, без расшифровки: имя ключа секретом не является.
        var secrets = module?.Settings.Where(s => s.Kind == IntegrationSettingKind.Secret).Select(s => s.Key).ToArray() ?? [];
        return new(x.Id, x.Module, module?.Name ?? x.Module, x.Name, IntegrationSync.Settings(x), x.ProtectedSecrets.Length > 0 ? secrets : [],
            x.Enabled, x.AutoSyncMinutes, x.LastRunAt, x.LastOk, x.LastMessage, linked);
    }

    private static IntegrationRunDto Run(TaskSourceRun x) =>
        new(x.Id, x.StartedAt, x.FinishedAt, x.Ok, x.Created, x.Updated, x.Unchanged, x.Skipped, x.Message);
}

/// <summary>Фоновая автосинхронизация: раз в минуту запускает подключения, у которых подошёл срок.</summary>
public sealed class IntegrationSyncWorker(IServiceScopeFactory scopes, ILogger<IntegrationSyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await Tick(stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Автосинхронизация интеграций не выполнилась"); }
        }
    }

    internal async Task Tick(CancellationToken ct)
    {
        List<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AlvaDbContext>();
            var now = DateTimeOffset.UtcNow;
            due = (await db.TaskSources.AsNoTracking().Where(x => x.Enabled && x.AutoSyncMinutes != null).ToListAsync(ct))
                .Where(x => x.LastRunAt is not { } last || last.AddMinutes(x.AutoSyncMinutes!.Value) <= now).Select(x => x.Id).ToList();
        }
        foreach (var id in due)
        {
            // Своя область на прогон: ошибка одного подключения не оставляет мусор в контексте следующего.
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AlvaDbContext>();
            if (await db.TaskSources.SingleOrDefaultAsync(x => x.Id == id, ct) is not { } connection) continue;
            try { await scope.ServiceProvider.GetRequiredService<IntegrationSync>().RunAsync(connection, full: false, ct); }
            catch (ApiException) { }
        }
    }
}
