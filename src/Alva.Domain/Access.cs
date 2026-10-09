namespace Alva.Domain;

public sealed class UserAccount : Entity
{
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
public sealed class WorkspaceMembership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public bool IsOwner { get; set; }
}
public sealed class TeamMembership
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
}
public sealed class PermissionGrant : Entity
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public string Area { get; set; } = "";
    public bool View { get; set; }
    public bool Edit { get; set; }
    public bool Manage { get; set; }
}
