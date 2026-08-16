using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

/// <summary>Workflow: Registered -> DocumentsPending -> Filed -> HearingScheduled -> DecisionReceived -> Closed.</summary>
public class LegalCase : BaseEntity, IDepartmentScoped
{
    public string CaseNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public Guid? OpposingClubId { get; set; }
    public Club? OpposingClub { get; set; }

    public LegalCaseType Type { get; set; }
    public LegalStatus Status { get; set; } = LegalStatus.Registered;
    public LegalOutcome Outcome { get; set; } = LegalOutcome.Pending;
    public CasePriority Priority { get; set; } = CasePriority.Medium;

    public string? LawyerName { get; set; }
    public string? LawyerFirm { get; set; }
    public Guid? AssignedCounselId { get; set; }
    public AppUser? AssignedCounsel { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? ClaimAmount { get; set; }
    public decimal? AwardedAmount { get; set; }
    public string Currency { get; set; } = "INR";

    public DateTime FiledAt { get; set; } = DateTime.UtcNow;
    public DateTime? HearingDate { get; set; }
    public DateTime? DecisionDate { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<LegalCaseEvent> Events { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}

public class LegalCaseEvent : BaseEntity
{
    public Guid LegalCaseId { get; set; }
    public LegalCase LegalCase { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public LegalStatus? StatusAtEvent { get; set; }
    public Guid? AuthorId { get; set; }
    public AppUser? Author { get; set; }
}
