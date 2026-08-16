using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace FpaiConnect.Domain.Entities;

/// <summary>Canonical role names. Authorization policies are built from these.</summary>
public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string DepartmentHead = "DepartmentHead";
    public const string Staff = "Staff";
    public const string ExternalAccountant = "ExternalAccountant";

    public static readonly string[] All = [SuperAdmin, DepartmentHead, Staff, ExternalAccountant];
}

/// <summary>Well-known department codes, seeded at startup and referenced by every scoped module.</summary>
public static class DepartmentCodes
{
    public const string Welfare = "WELFARE";
    public const string Legal = "LEGAL";
    public const string Finance = "FINANCE";
    public const string Governance = "GOVERNANCE";
    public const string Operations = "OPERATIONS";
    public const string Executive = "EXECUTIVE";

    public static readonly string[] All = [Welfare, Legal, Finance, Governance, Operations, Executive];
}

public class Department : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<AppUser> Users { get; set; } = [];
}

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Invited;
    public string? JobTitle { get; set; }
    /// <summary>Google subject id, set on first federated sign-in. Null for password-only accounts.</summary>
    public string? GoogleSubjectId { get; set; }
    /// <summary>Microsoft/Entra subject (oid) id, set on first federated sign-in via Microsoft.</summary>
    public string? MicrosoftSubjectId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsDeleted { get; set; }

    // ---- Self-registration and approval ----
    /// <summary>Who approved (or rejected) this account, and when. Null while pending.</summary>
    public Guid? ApprovedById { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Shown to the applicant if they are turned down.</summary>
    public string? ApprovalNote { get; set; }
    /// <summary>What the applicant said about themselves when registering.</summary>
    public string? RegistrationNote { get; set; }

    // ---- Interface preferences, stored per user so they follow them between devices ----
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    /// <summary>Key of a colour scheme defined in the frontend theme catalogue.</summary>
    public string ColorScheme { get; set; } = "pitch";
    /// <summary>Key of a font stack defined in the frontend theme catalogue.</summary>
    public string FontChoice { get; set; } = "sans";

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}

/// <summary>Append-only trail. Written by the DbContext interceptor on every state change.</summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Changes { get; set; }
}
