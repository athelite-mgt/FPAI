using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

public class Meeting : BaseEntity, IDepartmentScoped
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public MeetingType Type { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;

    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string? Location { get; set; }
    public string? VideoLink { get; set; }

    public string? Agenda { get; set; }
    public string? Minutes { get; set; }
    /// <summary>Minimum attendees required for motions in this meeting to be valid.</summary>
    public int QuorumRequired { get; set; } = 1;

    public Guid? ChairId { get; set; }
    public AppUser? Chair { get; set; }

    public ICollection<MeetingAttendee> Attendees { get; set; } = [];
    public ICollection<Motion> Motions { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}

public class MeetingAttendee : BaseEntity
{
    public Guid MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public AttendeeStatus Status { get; set; } = AttendeeStatus.Invited;
    public bool IsVotingMember { get; set; } = true;
}

/// <summary>A resolution put to the vote. Passed/Failed is decided when voting closes and quorum is checked.</summary>
public class Motion : BaseEntity
{
    public Guid MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MotionStatus Status { get; set; } = MotionStatus.Draft;
    public int SequenceNumber { get; set; }

    public DateTime? VotingOpensAt { get; set; }
    public DateTime? VotingClosesAt { get; set; }
    /// <summary>Fraction of non-abstaining votes needed to pass. 0.5 = simple majority, 0.667 = special resolution.</summary>
    public double PassThreshold { get; set; } = 0.5;
    /// <summary>Hides who voted which way from everyone except Super Admin.</summary>
    public bool IsSecretBallot { get; set; }

    public ICollection<Vote> Votes { get; set; } = [];
}

public class Vote : BaseEntity
{
    public Guid MotionId { get; set; }
    public Motion Motion { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public VoteChoice Choice { get; set; }
    public DateTime CastAt { get; set; } = DateTime.UtcNow;
}
