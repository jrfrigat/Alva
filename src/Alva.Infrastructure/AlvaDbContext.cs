using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Alva.Infrastructure;

public class AlvaDbContext(DbContextOptions<AlvaDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTeam> ProjectTeams => Set<ProjectTeam>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<TaskCard> Tasks => Set<TaskCard>();
    public DbSet<TaskSprint> TaskSprints => Set<TaskSprint>();
    public DbSet<Actor> Actors => Set<Actor>();
    public DbSet<ActorRole> ActorRoles => Set<ActorRole>();
    public DbSet<ActorDepartment> ActorDepartments => Set<ActorDepartment>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<TaskDependency> Dependencies => Set<TaskDependency>();
    public DbSet<WorkCalendar> Calendars => Set<WorkCalendar>();
    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();
    public DbSet<RecurringBlock> RecurringBlocks => Set<RecurringBlock>();
    public DbSet<TaskHistory> TaskHistory => Set<TaskHistory>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();

    /// <summary>
    /// Сохраняет изменения и в той же транзакции пересчитывает кэш TaskSprint для задач и спринтов, у которых изменились
    /// даты, архив или команда. Так кэш не обойдёт ни один путь записи: правка, перенос, рамка родителя, автобаланс, импорт.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var (tasks, sprints) = TaskSprintCache.Touched(ChangeTracker);
        if (tasks.Count == 0 && sprints.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        var own = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await TaskSprintCache.Forget(this, tasks, sprints, cancellationToken);
            var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await TaskSprintCache.Fill(this, tasks, sprints, cancellationToken);
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (own is not null) await own.CommitAsync(cancellationToken);
            return saved;
        }
        finally
        {
            if (own is not null) await own.DisposeAsync();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<UserAccount>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<WorkspaceMembership>().HasKey(x => new { x.WorkspaceId, x.UserId });
        b.Entity<WorkspaceMembership>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<WorkspaceMembership>().HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<TeamMembership>().HasKey(x => new { x.TeamId, x.UserId });
        b.Entity<TeamMembership>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<TeamMembership>().HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<PermissionGrant>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<PermissionGrant>().HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<PermissionGrant>().HasIndex(x => new { x.TeamId, x.UserId, x.Area }).IsUnique();
        b.Entity<Actor>().HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<ProjectTeam>().HasKey(x => new { x.ProjectId, x.TeamId });
        b.Entity<ActorRole>().HasKey(x => new { x.ActorId, x.RoleId });
        b.Entity<ActorDepartment>().HasKey(x => new { x.ActorId, x.DepartmentId });
        b.Entity<ActorDepartment>().HasOne<Actor>().WithMany().HasForeignKey(x => x.ActorId);
        b.Entity<ActorDepartment>().HasOne<Actor>().WithMany().HasForeignKey(x => x.DepartmentId);
        b.Entity<Team>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<Project>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<Team>().HasOne<Project>().WithMany().HasForeignKey(x => x.DefaultProjectId);
        b.Entity<ProjectTeam>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        b.Entity<ProjectTeam>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<Sprint>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<TaskCard>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<TaskCard>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        b.Entity<TaskCard>().HasOne<TaskCard>().WithMany().HasForeignKey(x => x.ParentId);
        b.Entity<Actor>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<Actor>().HasOne<Actor>().WithMany().HasForeignKey(x => x.ParentId);
        b.Entity<ActorRole>().HasOne<Actor>().WithMany().HasForeignKey(x => x.ActorId);
        b.Entity<ActorRole>().HasOne<Actor>().WithMany().HasForeignKey(x => x.RoleId);
        b.Entity<Assignment>().HasOne<TaskCard>().WithMany().HasForeignKey(x => x.TaskId);
        b.Entity<Assignment>().HasOne<Actor>().WithMany().HasForeignKey(x => x.TargetId);
        b.Entity<Assignment>().HasOne<Actor>().WithMany().HasForeignKey(x => x.ResolvedPersonId);
        b.Entity<TaskDependency>().HasOne<TaskCard>().WithMany().HasForeignKey(x => x.PredecessorId);
        b.Entity<TaskDependency>().HasOne<TaskCard>().WithMany().HasForeignKey(x => x.SuccessorId);
        b.Entity<WorkCalendar>().HasOne<Actor>().WithMany().HasForeignKey(x => x.ActorId);
        b.Entity<CalendarException>().HasOne<WorkCalendar>().WithMany().HasForeignKey(x => x.CalendarId);
        b.Entity<RecurringBlock>().HasOne<WorkCalendar>().WithMany().HasForeignKey(x => x.CalendarId);
        b.Entity<TaskHistory>().HasOne<TaskCard>().WithMany().HasForeignKey(x => x.TaskId);
        b.Entity<TaskCard>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<TaskCard>().HasIndex(x => new { x.TeamId, x.ParentId, x.Order });
        b.Entity<TaskCard>().HasIndex(x => new { x.TeamId, x.StartDate, x.EndDate });
        b.Entity<TaskSprint>().HasKey(x => new { x.TaskId, x.SprintId });
        b.Entity<TaskSprint>().HasIndex(x => new { x.TeamId, x.SprintId });
        b.Entity<TaskSprint>().HasOne<TaskCard>().WithMany().HasForeignKey(x => x.TaskId);
        b.Entity<TaskSprint>().HasOne<Sprint>().WithMany().HasForeignKey(x => x.SprintId);
        b.Entity<Assignment>().HasIndex(x => x.TaskId).IsUnique();
        b.Entity<WorkCalendar>().HasIndex(x => x.ActorId).IsUnique();
        b.Entity<TaskDependency>().HasIndex(x => new { x.PredecessorId, x.SuccessorId }).IsUnique();
        b.Entity<TaskCard>().ToTable(t => { t.HasCheckConstraint("CK_Task_Estimate", "\"EstimateHours\" IS NULL OR \"EstimateHours\" >= 0"); t.HasCheckConstraint("CK_Task_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\""); });
        b.Entity<Sprint>().ToTable(t => t.HasCheckConstraint("CK_Sprint_Dates", "\"EndDate\" >= \"StartDate\""));
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(string)) property.SetMaxLength(property.Name is "Description" or "Summary" ? 10000 : 500);
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)) { property.SetPrecision(12); property.SetScale(2); }
            }
            foreach (var fk in entity.GetForeignKeys()) fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}

public sealed class AlvaDbContextFactory : IDesignTimeDbContextFactory<AlvaDbContext>
{
    public AlvaDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AlvaDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Alva") ?? "Host=localhost;Database=alva;Username=alva").Options);
}
