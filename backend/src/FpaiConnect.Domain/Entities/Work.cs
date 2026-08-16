using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

public class WorkTask : BaseEntity, IDepartmentScoped
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Todo;
    public CasePriority Priority { get; set; } = CasePriority.Medium;
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Guid? AssigneeId { get; set; }
    public AppUser? Assignee { get; set; }

    /// <summary>Free-form link back to the record that spawned this task, e.g. "WelfareCase".</summary>
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
}

/// <summary>
/// Single-step approval: exactly one approver decides. The approver must be a Department Head of the
/// owning department, or a Super Admin.
/// </summary>
public class ApprovalRequest : BaseEntity, IDepartmentScoped
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Entity awaiting approval, e.g. "Voucher" / "Expense". Drives the post-approval side effect.</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public decimal? Amount { get; set; }

    public Guid? RequestedById { get; set; }
    public AppUser? RequestedBy { get; set; }
    public Guid? DecidedById { get; set; }
    public AppUser? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
}

/// <summary>In-app notification, raised on assignment, approval decisions and voting invitations.</summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? Link { get; set; }
    public bool IsRead { get; set; }
}
