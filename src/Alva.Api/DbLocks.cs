using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alva.Api;

/// <summary>
/// Locks the parent row while the reference data is being changed so that concurrent edits are queued.
/// SQLite has no FOR UPDATE: there, a no-op write in the same transaction immediately takes the database write lock and gives the same ordering.
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
