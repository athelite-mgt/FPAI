using FpaiConnect.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace FpaiConnect.Application.Dtos;

// ---------------------------------------------------------------- shared
public record LookupDto(Guid Id, string Label, string? Sub = null);

public record StatusTransitionRequest
{
    [Required, MaxLength(40)] public string Status { get; init; } = string.Empty;
    [MaxLength(2000)] public string? Comment { get; init; }
}

// ---------------------------------------------------------------- directory
public record DepartmentDto(Guid Id, string Code, string Name, string? Description, int UserCount);

public record DepartmentUpsertRequest
{
    /// <summary>Short uppercase key, e.g. WELFARE. Built-in codes cannot be changed.</summary>
    [Required, MaxLength(30), RegularExpression("^[A-Za-z0-9_-]+$",
        ErrorMessage = "A code may contain only letters, numbers, hyphens and underscores.")]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(120)] public string Name { get; init; } = string.Empty;

    [MaxLength(500)] public string? Description { get; init; }
}

public record ClubDto(Guid Id, string Name, string? City, string? League, int PlayerCount);

public record ClubUpsertRequest
{
    [Required, MaxLength(150)] public string Name { get; init; } = string.Empty;
    [MaxLength(100)] public string? City { get; init; }
    [MaxLength(100)] public string? League { get; init; }
}

public record PlayerDto(
    Guid Id, string MembershipId, string FullName, DateOnly? DateOfBirth, string? Position,
    string Nationality, Guid? CurrentClubId, string? CurrentClubName, int? JerseyNumber,
    string? ContactEmail, string? ContactPhone, PlayerStatus Status,
    int WelfareCaseCount, int LegalCaseCount);

public record PlayerUpsertRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    [MaxLength(60)] public string? Position { get; init; }
    [MaxLength(80)] public string Nationality { get; init; } = "India";
    public Guid? CurrentClubId { get; init; }
    [Range(1, 99)] public int? JerseyNumber { get; init; }
    [EmailAddress, MaxLength(200)] public string? ContactEmail { get; init; }
    [MaxLength(40)] public string? ContactPhone { get; init; }
    public PlayerStatus Status { get; init; } = PlayerStatus.Active;
}

public record VendorDto(Guid Id, string Name, string? GstNumber, string? ContactEmail,
    string? ContactPhone, string? BankAccount, int VoucherCount);

public record VendorUpsertRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = string.Empty;
    [MaxLength(30)] public string? GstNumber { get; init; }
    [EmailAddress, MaxLength(200)] public string? ContactEmail { get; init; }
    [MaxLength(40)] public string? ContactPhone { get; init; }
    [MaxLength(60)] public string? BankAccount { get; init; }
}

// ---------------------------------------------------------------- welfare
public record WelfareCaseListDto(
    Guid Id, string CaseNumber, string Title, Guid PlayerId, string PlayerName,
    WelfareCategory Category, CasePriority Priority, WelfareStatus Status,
    Guid? AssignedOfficerId, string? AssignedOfficerName, bool IsDispute,
    DateTime OpenedAt, DateTime? ResolvedAt);

public record WelfareCaseDetailDto(
    Guid Id, string CaseNumber, string Title, string? Description, string? Resolution,
    Guid PlayerId, string PlayerName, string? PlayerClub, Guid DepartmentId, string DepartmentName,
    WelfareCategory Category, CasePriority Priority, WelfareStatus Status,
    Guid? AssignedOfficerId, string? AssignedOfficerName, bool IsDispute,
    DateTime OpenedAt, DateTime? ResolvedAt, DateTime? ClosedAt,
    IReadOnlyList<CaseNoteDto> Notes, IReadOnlyList<DocumentListDto> Documents,
    IReadOnlyList<string> AllowedTransitions);

public record CaseNoteDto(Guid Id, string Note, string? StatusAtNote,
    string? AuthorName, DateTime CreatedAt);

public record WelfareCaseUpsertRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    [Required] public Guid PlayerId { get; init; }
    public WelfareCategory Category { get; init; }
    public CasePriority Priority { get; init; } = CasePriority.Medium;
    public Guid? AssignedOfficerId { get; init; }
    public bool IsDispute { get; init; }
    [MaxLength(4000)] public string? Description { get; init; }
    [MaxLength(4000)] public string? Resolution { get; init; }
}

public record AddNoteRequest
{
    [Required, MaxLength(4000)] public string Note { get; init; } = string.Empty;
}

// ---------------------------------------------------------------- legal
public record LegalCaseListDto(
    Guid Id, string CaseNumber, string Title, Guid PlayerId, string PlayerName,
    string? OpposingClubName, LegalCaseType Type, LegalStatus Status, LegalOutcome Outcome,
    CasePriority Priority, string? LawyerName, decimal? ClaimAmount, string Currency,
    DateTime FiledAt, DateTime? HearingDate);

public record LegalCaseDetailDto(
    Guid Id, string CaseNumber, string Title, string? Description,
    Guid PlayerId, string PlayerName, Guid? OpposingClubId, string? OpposingClubName,
    Guid DepartmentId, string DepartmentName, LegalCaseType Type, LegalStatus Status,
    LegalOutcome Outcome, CasePriority Priority, string? LawyerName, string? LawyerFirm,
    Guid? AssignedCounselId, string? AssignedCounselName,
    decimal? ClaimAmount, decimal? AwardedAmount, string Currency,
    DateTime FiledAt, DateTime? HearingDate, DateTime? DecisionDate,
    DateTime? ClosedAt, IReadOnlyList<LegalEventDto> Events,
    IReadOnlyList<DocumentListDto> Documents, IReadOnlyList<string> AllowedTransitions);

public record LegalEventDto(Guid Id, string Title, string? Detail,
    DateTime OccurredAt, string? StatusAtEvent, string? AuthorName);

public record LegalCaseUpsertRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    [Required] public Guid PlayerId { get; init; }
    public Guid? OpposingClubId { get; init; }
    public LegalCaseType Type { get; init; }
    public CasePriority Priority { get; init; } = CasePriority.Medium;
    [MaxLength(150)] public string? LawyerName { get; init; }
    [MaxLength(200)] public string? LawyerFirm { get; init; }
    public Guid? AssignedCounselId { get; init; }
    [Range(0, 999999999)] public decimal? ClaimAmount { get; init; }
    [Range(0, 999999999)] public decimal? AwardedAmount { get; init; }
    public LegalOutcome Outcome { get; init; } = LegalOutcome.Pending;
    public DateTime? HearingDate { get; init; }
    [MaxLength(4000)] public string? Description { get; init; }
}

public record AddLegalEventRequest
{
    [Required, MaxLength(200)] public string Title { get; init; } = string.Empty;
    [MaxLength(2000)] public string? Detail { get; init; }
    public DateTime? OccurredAt { get; init; }
}

// ---------------------------------------------------------------- finance
public record VoucherListDto(
    Guid Id, string VoucherNumber, Guid VendorId, string VendorName,
    Guid DepartmentId, string DepartmentName, decimal Amount, decimal TaxAmount,
    decimal TotalAmount, string Currency, VoucherStatus Status,
    DateTime VoucherDate, int OpenQueryCount);

public record VoucherDetailDto(
    Guid Id, string VoucherNumber, Guid VendorId, string VendorName,
    Guid DepartmentId, string DepartmentName, decimal Amount, decimal TaxAmount,
    decimal TotalAmount, string Currency, VoucherStatus Status, DateTime VoucherDate,
    string? Description, string? ApprovedByName, DateTime? ApprovedAt,
    string? RejectionReason, string? ReconciledByName, DateTime? ReconciledAt,
    IReadOnlyList<AccountantQueryDto> Queries, IReadOnlyList<DocumentListDto> Documents,
    IReadOnlyList<string> AllowedTransitions);

public record VoucherUpsertRequest
{
    [Required] public Guid VendorId { get; init; }
    [Required] public Guid DepartmentId { get; init; }
    [Range(0.01, 999999999)] public decimal Amount { get; init; }
    [Range(0, 999999999)] public decimal TaxAmount { get; init; }
    public DateTime? VoucherDate { get; init; }
    [MaxLength(2000)] public string? Description { get; init; }
}

public record ExpenseListDto(
    Guid Id, string ExpenseNumber, string Title, string? Category,
    Guid DepartmentId, string DepartmentName, decimal Amount, string Currency,
    ExpenseStatus Status, DateTime IncurredOn, string? SubmittedByName, int InvoiceCount);

public record ExpenseDetailDto(
    Guid Id, string ExpenseNumber, string Title, string? Category, string? Description,
    Guid DepartmentId, string DepartmentName, decimal Amount, string Currency,
    ExpenseStatus Status, DateTime IncurredOn, string? SubmittedByName,
    string? ApprovedByName, DateTime? ApprovedAt, string? RejectionReason,
    IReadOnlyList<InvoiceDto> Invoices, IReadOnlyList<AccountantQueryDto> Queries,
    IReadOnlyList<DocumentListDto> Documents, IReadOnlyList<string> AllowedTransitions);

public record ExpenseUpsertRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    [Required] public Guid DepartmentId { get; init; }
    [MaxLength(100)] public string? Category { get; init; }
    [Range(0.01, 999999999)] public decimal Amount { get; init; }
    public DateTime? IncurredOn { get; init; }
    [MaxLength(2000)] public string? Description { get; init; }
}

public record InvoiceDto(
    Guid Id, string InvoiceNumber, Guid? VendorId, string? VendorName, Guid? ExpenseId,
    decimal Amount, decimal TaxAmount, string Currency, InvoiceStatus Status,
    DateTime IssuedOn, DateTime? DueDate, DateTime? PaidOn);

public record InvoiceUpsertRequest
{
    public Guid? VendorId { get; init; }
    public Guid? ExpenseId { get; init; }
    [Range(0.01, 999999999)] public decimal Amount { get; init; }
    [Range(0, 999999999)] public decimal TaxAmount { get; init; }
    public DateTime? IssuedOn { get; init; }
    public DateTime? DueDate { get; init; }
    public InvoiceStatus Status { get; init; } = InvoiceStatus.Received;
}

public record AccountantQueryDto(
    Guid Id, Guid? VoucherId, string? VoucherNumber, Guid? ExpenseId, string? ExpenseNumber,
    string Question, string? Response, QueryStatus Status,
    string? RaisedByName, string? AnsweredByName,
    DateTime CreatedAt, DateTime? AnsweredAt);

public record RaiseQueryRequest
{
    public Guid? VoucherId { get; init; }
    public Guid? ExpenseId { get; init; }
    [Required, MaxLength(2000)] public string Question { get; init; } = string.Empty;
}

public record AnswerQueryRequest
{
    [Required, MaxLength(2000)] public string Response { get; init; } = string.Empty;
}

public record FinanceSummaryDto(
    decimal MonthlyIncome, decimal MonthlyExpense, int PendingVouchers, int OpenQueries,
    IReadOnlyList<MonthlyTrendPoint> Trend, IReadOnlyList<DepartmentSpendDto> ByDepartment);

public record MonthlyTrendPoint(int Year, int Month, string Label, decimal Income, decimal Expense);

public record DepartmentSpendDto(Guid DepartmentId, string DepartmentName,
    decimal Spent, decimal Budgeted);

// ---------------------------------------------------------------- governance
public record MeetingListDto(
    Guid Id, string ReferenceNumber, string Title, MeetingType Type, MeetingStatus Status,
    DateTime ScheduledAt, int DurationMinutes, string? Location, string? ChairName,
    int AttendeeCount, int MotionCount);

public record MeetingDetailDto(
    Guid Id, string ReferenceNumber, string Title, MeetingType Type, MeetingStatus Status,
    DateTime ScheduledAt, int DurationMinutes, string? Location, string? VideoLink,
    string? Agenda, string? Minutes, int QuorumRequired, Guid DepartmentId, string DepartmentName,
    Guid? ChairId, string? ChairName, IReadOnlyList<AttendeeDto> Attendees,
    IReadOnlyList<MotionDto> Motions, IReadOnlyList<DocumentListDto> Documents,
    bool QuorumMet, IReadOnlyList<string> AllowedTransitions);

public record AttendeeDto(Guid Id, Guid UserId, string UserName, string? DepartmentName,
    AttendeeStatus Status, bool IsVotingMember);

public record MotionDto(
    Guid Id, Guid MeetingId, string Title, string? Description, MotionStatus Status,
    int SequenceNumber, DateTime? VotingOpensAt, DateTime? VotingClosesAt,
    double PassThreshold, bool IsSecretBallot,
    int VotesFor, int VotesAgainst, int VotesAbstain, int EligibleVoters,
    VoteChoice? MyVote, bool CanVote, IReadOnlyList<VoteDto> Votes);

public record VoteDto(Guid Id, Guid UserId, string UserName, VoteChoice Choice, DateTime CastAt);

public record MeetingUpsertRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    public MeetingType Type { get; init; }
    [Required] public DateTime ScheduledAt { get; init; }
    [Range(15, 600)] public int DurationMinutes { get; init; } = 60;
    [MaxLength(200)] public string? Location { get; init; }
    [MaxLength(500)] public string? VideoLink { get; init; }
    [MaxLength(8000)] public string? Agenda { get; init; }
    [MaxLength(20000)] public string? Minutes { get; init; }
    [Range(1, 100)] public int QuorumRequired { get; init; } = 1;
    public Guid? ChairId { get; init; }
    public IReadOnlyList<Guid>? AttendeeUserIds { get; init; }
}

public record MotionUpsertRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; init; }
    public DateTime? VotingOpensAt { get; init; }
    public DateTime? VotingClosesAt { get; init; }
    [Range(0.5, 1.0)] public double PassThreshold { get; init; } = 0.5;
    public bool IsSecretBallot { get; init; }
}

public record CastVoteRequest
{
    [Required] public VoteChoice Choice { get; init; }
}

public record AttendanceRequest
{
    [Required] public AttendeeStatus Status { get; init; }
}

// ---------------------------------------------------------------- operations
public record EventListDto(
    Guid Id, string ReferenceNumber, string Name, EventType Type, EventStatus Status,
    DateTime StartDate, DateTime? EndDate, string? Venue, string? City,
    decimal BudgetAmount, decimal ActualCost, int ExpectedAttendees, int ActualAttendees,
    string? OwnerName, int ParticipantCount);

public record EventDetailDto(
    Guid Id, string ReferenceNumber, string Name, EventType Type, EventStatus Status,
    string? Description, DateTime StartDate, DateTime? EndDate,
    string? Venue, string? City, decimal BudgetAmount, decimal ActualCost,
    int ExpectedAttendees, int ActualAttendees, Guid DepartmentId, string DepartmentName,
    Guid? OwnerId, string? OwnerName, IReadOnlyList<EventParticipantDto> Participants,
    IReadOnlyList<DocumentListDto> Documents, IReadOnlyList<string> AllowedTransitions);

public record EventParticipantDto(Guid Id, Guid PlayerId, string PlayerName,
    string? ClubName, AttendeeStatus Status, string? Notes);

public record EventUpsertRequest
{
    [Required, MaxLength(250)] public string Name { get; init; } = string.Empty;
    public EventType Type { get; init; }
    [Required] public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    [MaxLength(200)] public string? Venue { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    [Range(0, 999999999)] public decimal BudgetAmount { get; init; }
    [Range(0, 999999999)] public decimal ActualCost { get; init; }
    [Range(0, 100000)] public int ExpectedAttendees { get; init; }
    [Range(0, 100000)] public int ActualAttendees { get; init; }
    public Guid? OwnerId { get; init; }
    [MaxLength(4000)] public string? Description { get; init; }
}

public record AddParticipantsRequest
{
    [Required, MinLength(1)] public IReadOnlyList<Guid> PlayerIds { get; init; } = [];
}

// ---------------------------------------------------------------- documents
public record DocumentListDto(
    Guid Id, string Title, string FileName, string ContentType, long SizeBytes,
    DocumentCategory Category, bool IsConfidential, int Version,
    Guid DepartmentId, string? DepartmentName, string? UploadedByName,
    DateTime CreatedAt, string? LinkedTo, Guid? LinkedId);

public record DocumentUpdateRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    public DocumentCategory Category { get; init; }
    public bool IsConfidential { get; init; }
    [MaxLength(2000)] public string? Description { get; init; }
}

// ---------------------------------------------------------------- tasks & approvals
public record WorkTaskDto(
    Guid Id, string ReferenceNumber, string Title, string? Description,
    WorkTaskStatus Status, CasePriority Priority, Guid DepartmentId, string DepartmentName,
    Guid? AssigneeId, string? AssigneeName, DateTime? DueDate,
    DateTime? CompletedAt, DateTime CreatedAt, bool IsOverdue,
    string? RelatedEntityType, Guid? RelatedEntityId, IReadOnlyList<string> AllowedTransitions);

public record WorkTaskUpsertRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; init; }
    [Required] public Guid DepartmentId { get; init; }
    public CasePriority Priority { get; init; } = CasePriority.Medium;
    public Guid? AssigneeId { get; init; }
    public DateTime? DueDate { get; init; }
    [MaxLength(60)] public string? RelatedEntityType { get; init; }
    public Guid? RelatedEntityId { get; init; }
}

public record ApprovalRequestDto(
    Guid Id, string ReferenceNumber, string Title, string? Description, ApprovalStatus Status,
    string EntityType, Guid EntityId, decimal? Amount, Guid DepartmentId, string DepartmentName,
    string? RequestedByName, string? DecidedByName, DateTime CreatedAt,
    DateTime? DecidedAt, string? DecisionComment, bool CanDecide);

public record CreateApprovalRequest
{
    [Required, MaxLength(250)] public string Title { get; init; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; init; }
    [Required, MaxLength(60)] public string EntityType { get; init; } = string.Empty;
    [Required] public Guid EntityId { get; init; }
    [Required] public Guid DepartmentId { get; init; }
    [Range(0, 999999999)] public decimal? Amount { get; init; }
}

public record ApprovalDecisionRequest
{
    [Required] public bool Approve { get; init; }
    [MaxLength(2000)] public string? Comment { get; init; }
}

public record NotificationDto(Guid Id, string Title, string? Body, string? Link,
    bool IsRead, DateTime CreatedAt);

// ---------------------------------------------------------------- users
public record UserListDto(
    Guid Id, string FullName, string Email, string? JobTitle, Guid? DepartmentId,
    string? DepartmentName, IReadOnlyList<string> Roles, UserStatus Status,
    bool HasGoogleLinked, DateTime CreatedAt, DateTime? LastLoginAt);

public record CreateUserRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = string.Empty;
    [Required, EmailAddress, MaxLength(200)] public string Email { get; init; } = string.Empty;
    [MaxLength(150)] public string? JobTitle { get; init; }
    public Guid? DepartmentId { get; init; }
    [Required] public string Role { get; init; } = string.Empty;
    /// <summary>Optional initial password. When omitted the account is created as Invited for Google sign-in.</summary>
    [MinLength(10), MaxLength(200)] public string? Password { get; init; }
}

public record UpdateUserRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = string.Empty;
    [MaxLength(150)] public string? JobTitle { get; init; }
    public Guid? DepartmentId { get; init; }
    [Required] public string Role { get; init; } = string.Empty;
    public UserStatus Status { get; init; }
}

public record ResetPasswordRequest
{
    [Required, MinLength(10), MaxLength(200)] public string NewPassword { get; init; } = string.Empty;
}

public record RoleDto(string Name, string? Description, int UserCount);

// ---------------------------------------------------------------- dashboard & reports
public record DashboardDto(
    int ActiveWelfareCases, int ActiveLegalMatters, decimal MonthlyExpense,
    int UpcomingMeetings, int PendingTasks, int PendingApprovals,
    IReadOnlyList<CountByLabel> WelfareByStatus, IReadOnlyList<CountByLabel> LegalByType,
    IReadOnlyList<MonthlyTrendPoint> FinanceTrend, IReadOnlyList<ParticipationPoint> VotingTrend,
    IReadOnlyList<ActivityDto> RecentActivity);

public record CountByLabel(string Label, int Count);

public record ParticipationPoint(string Label, double ParticipationRate, int MotionsClosed);

public record ActivityDto(string EntityName, string Action, string Summary,
    string? UserName, DateTime Timestamp);

/// <summary>Full audit trail row, for the admin-only Audit Log page (unlike the trimmed
/// ActivityDto used for the dashboard's recent-activity widget).</summary>
public record AuditEntryDto(Guid Id, string EntityName, string EntityId, string Action,
    Guid? UserId, string? UserName, DateTime Timestamp, string? Changes);

public record ReportSummaryDto(
    string Title, string Description, IReadOnlyList<CountByLabel> Rows, decimal? Total);
