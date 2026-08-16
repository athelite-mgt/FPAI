using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

public class Event : BaseEntity, IDepartmentScoped
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public EventType Type { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Planned;
    public string? Description { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Venue { get; set; }
    public string? City { get; set; }

    public decimal BudgetAmount { get; set; }
    public decimal ActualCost { get; set; }
    public int ExpectedAttendees { get; set; }
    public int ActualAttendees { get; set; }

    public Guid? OwnerId { get; set; }
    public AppUser? Owner { get; set; }

    public ICollection<EventParticipant> Participants { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}

public class EventParticipant : BaseEntity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public AttendeeStatus Status { get; set; } = AttendeeStatus.Invited;
    public string? Notes { get; set; }
}
