using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

/// <summary>Workflow: New -> UnderReview -> Assigned -> InProgress -> Resolved -> Closed.</summary>
public class WelfareCase : BaseEntity, IDepartmentScoped
{
    public string CaseNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public WelfareCategory Category { get; set; }
    public CasePriority Priority { get; set; } = CasePriority.Medium;
    public WelfareStatus Status { get; set; } = WelfareStatus.New;

    public Guid? AssignedOfficerId { get; set; }
    public AppUser? AssignedOfficer { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Resolution { get; set; }

    /// <summary>True when the case was raised as a formal dispute rather than a welfare request.</summary>
    public bool IsDispute { get; set; }

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<WelfareCaseNote> Notes { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}

public class WelfareCaseNote : BaseEntity
{
    public Guid WelfareCaseId { get; set; }
    public WelfareCase WelfareCase { get; set; } = null!;

    public string Note { get; set; } = string.Empty;
    /// <summary>Set when the note records a status transition, so the timeline can render it distinctly.</summary>
    public WelfareStatus? StatusAtNote { get; set; }
    public Guid? AuthorId { get; set; }
    public AppUser? Author { get; set; }
}
