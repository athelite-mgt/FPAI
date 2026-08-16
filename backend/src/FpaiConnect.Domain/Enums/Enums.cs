namespace FpaiConnect.Domain.Enums;

// ---------- Identity ----------
/// <summary>
/// PendingApproval is the state a self-registered account sits in. Such an account is never
/// issued an access token, so it can read nothing at all until an administrator approves it.
/// Invited is the older path: created by an admin, awaiting first sign-in.
/// </summary>
public enum UserStatus { Invited = 0, Active = 1, Suspended = 2, PendingApproval = 3, Rejected = 4 }

/// <summary>How the interface resolves light and dark.</summary>
public enum ThemeMode { System = 0, Light = 1, Dark = 2 }

// ---------- Player directory ----------
public enum PlayerStatus { Active = 0, Retired = 1, Injured = 2, FreeAgent = 3 }

// ---------- Welfare ----------
public enum WelfareCategory { Medical = 0, Contract = 1, Salary = 2, MentalHealth = 3, Travel = 4, Accommodation = 5 }
public enum WelfareStatus { New = 0, UnderReview = 1, Assigned = 2, InProgress = 3, Resolved = 4, Closed = 5 }
public enum CasePriority { Low = 0, Medium = 1, High = 2, Critical = 3 }

// ---------- Legal ----------
public enum LegalCaseType { FifaDrc = 0, Cas = 1, Psc = 2, Arbitration = 3 }
public enum LegalStatus { Registered = 0, DocumentsPending = 1, Filed = 2, HearingScheduled = 3, DecisionReceived = 4, Closed = 5 }
public enum LegalOutcome { Pending = 0, Won = 1, Lost = 2, Settled = 3, Withdrawn = 4 }

// ---------- Finance ----------
public enum VoucherStatus { Draft = 0, Pending = 1, Approved = 2, Rejected = 3, Reconciled = 4, Closed = 5 }
public enum ExpenseStatus { Created = 0, InvoiceAttached = 1, PendingApproval = 2, AccountantReview = 3, Reconciled = 4, Closed = 5, Rejected = 6 }
public enum InvoiceStatus { Received = 0, Verified = 1, Paid = 2, Disputed = 3 }
public enum QueryStatus { Open = 0, Answered = 1, Resolved = 2 }
public enum LedgerDirection { Income = 0, Expense = 1 }

// ---------- Meetings & voting ----------
public enum MeetingType { Board = 0, GeneralBody = 1, Committee = 2, Emergency = 3 }
public enum MeetingStatus { Scheduled = 0, InProgress = 1, Completed = 2, Cancelled = 3 }
public enum AttendeeStatus { Invited = 0, Accepted = 1, Declined = 2, Attended = 3, Absent = 4 }
public enum MotionStatus { Draft = 0, Open = 1, Passed = 2, Failed = 3, Withdrawn = 4 }
public enum VoteChoice { For = 0, Against = 1, Abstain = 2 }

// ---------- Events & operations ----------
public enum EventType { Workshop = 0, Camp = 1, Outreach = 2, Ceremony = 3, Tournament = 4 }
public enum EventStatus { Planned = 0, Dispatched = 1, Ongoing = 2, Completed = 3, Cancelled = 4 }

// ---------- Documents ----------
public enum DocumentCategory { Contract = 0, Legal = 1, Medical = 2, Financial = 3, Policy = 4, Minutes = 5, Identity = 6, Other = 7 }

// ---------- Tasks & approvals ----------
public enum WorkTaskStatus { Todo = 0, InProgress = 1, Blocked = 2, Done = 3, Cancelled = 4 }
public enum ApprovalStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }
