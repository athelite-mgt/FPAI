using FpaiConnect.Application.Abstractions;
using FpaiConnect.Domain.Entities;
using System.Security.Claims;

namespace FpaiConnect.Api.Security;

public static class AppClaims
{
    public const string DepartmentId = "dept";
    public const string FullName = "fullname";
}

/// <summary>
/// Resolves the caller from the validated JWT. All row-scoping decisions in the API funnel
/// through the Can*Department methods here, so the rules live in exactly one place.
/// </summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(AppClaims.FullName)
                               ?? Principal?.FindFirstValue(ClaimTypes.Name);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public Guid? DepartmentId =>
        Guid.TryParse(Principal?.FindFirstValue(AppClaims.DepartmentId), out var id) ? id : null;

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    public bool IsSuperAdmin => IsInRole(RoleNames.SuperAdmin);

    /// <summary>
    /// Super Admin and Department Heads have organisation-wide visibility.
    /// Staff are confined to their own department. The External Accountant is allowed
    /// through here because the module policy already restricts them to finance endpoints.
    /// </summary>
    public bool CanReadDepartment(Guid departmentId)
    {
        if (!IsAuthenticated) return false;
        if (IsSuperAdmin || IsInRole(RoleNames.DepartmentHead) || IsInRole(RoleNames.ExternalAccountant))
            return true;
        return DepartmentId == departmentId;
    }

    /// <summary>Writes are confined to the caller's own department, except for Super Admin.</summary>
    public bool CanWriteDepartment(Guid departmentId)
    {
        if (!IsAuthenticated) return false;
        if (IsSuperAdmin) return true;
        if (IsInRole(RoleNames.ExternalAccountant)) return false;
        if (IsInRole(RoleNames.DepartmentHead) || IsInRole(RoleNames.Staff))
            return DepartmentId == departmentId;
        return false;
    }

    /// <summary>Only a Super Admin, or the head of the owning department, may approve.</summary>
    public bool CanApproveDepartment(Guid departmentId)
    {
        if (!IsAuthenticated) return false;
        if (IsSuperAdmin) return true;
        return IsInRole(RoleNames.DepartmentHead) && DepartmentId == departmentId;
    }
}
