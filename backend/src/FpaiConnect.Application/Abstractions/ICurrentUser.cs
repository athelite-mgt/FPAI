namespace FpaiConnect.Application.Abstractions;

/// <summary>Ambient identity of the caller, resolved per request from the JWT.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    Guid? DepartmentId { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }

    bool IsInRole(string role);
    bool IsSuperAdmin { get; }
    /// <summary>True when the caller may read records belonging to <paramref name="departmentId"/>.</summary>
    bool CanReadDepartment(Guid departmentId);
    /// <summary>True when the caller may create/modify records belonging to <paramref name="departmentId"/>.</summary>
    bool CanWriteDepartment(Guid departmentId);
    /// <summary>True when the caller may approve records belonging to <paramref name="departmentId"/>.</summary>
    bool CanApproveDepartment(Guid departmentId);
}
