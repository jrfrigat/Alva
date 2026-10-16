using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alva.Api;

/// <summary>
/// Блокировка родительской строки на время изменения справочника, чтобы параллельные правки выстраивались в очередь.
/// В SQLite нет FOR UPDATE: там пустая запись в той же транзакции сразу берёт write-lock базы и даёт тот же порядок.
/// </summary>
public static class DbLocks
{
    public static Task LockTeam(AlvaDbContext db, Guid teamId) => db.Database.IsSqlite()
        ? db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Teams\" SET \"Id\" = \"Id\" WHERE \"Id\" = {teamId}")
        : db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Teams\" WHERE \"Id\" = {teamId} FOR UPDATE");

    public static Task LockWorkspace(AlvaDbContext db, Guid workspaceId) => db.Database.IsSqlite()
        ? db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Workspaces\" SET \"Id\" = \"Id\" WHERE \"Id\" = {workspaceId}")
        : db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Workspaces\" WHERE \"Id\" = {workspaceId} FOR UPDATE");
}
