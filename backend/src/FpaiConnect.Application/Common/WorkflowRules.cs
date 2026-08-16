using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Application.Common;

/// <summary>
/// Legal transitions for every workflow in the system. Enforced in the API so a client
/// cannot jump a record straight from New to Closed, and reused by the UI to decide
/// which actions to offer.
/// </summary>
public static class WorkflowRules
{
    public static readonly IReadOnlyDictionary<WelfareStatus, WelfareStatus[]> Welfare =
        new Dictionary<WelfareStatus, WelfareStatus[]>
        {
            [WelfareStatus.New] = [WelfareStatus.UnderReview, WelfareStatus.Assigned],
            [WelfareStatus.UnderReview] = [WelfareStatus.Assigned, WelfareStatus.InProgress, WelfareStatus.Closed],
            [WelfareStatus.Assigned] = [WelfareStatus.InProgress, WelfareStatus.UnderReview],
            [WelfareStatus.InProgress] = [WelfareStatus.Resolved, WelfareStatus.Assigned],
            [WelfareStatus.Resolved] = [WelfareStatus.Closed, WelfareStatus.InProgress],
            [WelfareStatus.Closed] = [WelfareStatus.InProgress]
        };

    public static readonly IReadOnlyDictionary<LegalStatus, LegalStatus[]> Legal =
        new Dictionary<LegalStatus, LegalStatus[]>
        {
            [LegalStatus.Registered] = [LegalStatus.DocumentsPending, LegalStatus.Filed],
            [LegalStatus.DocumentsPending] = [LegalStatus.Filed, LegalStatus.Registered],
            [LegalStatus.Filed] = [LegalStatus.HearingScheduled, LegalStatus.DecisionReceived],
            [LegalStatus.HearingScheduled] = [LegalStatus.DecisionReceived, LegalStatus.Filed],
            [LegalStatus.DecisionReceived] = [LegalStatus.Closed],
            [LegalStatus.Closed] = []
        };

    public static readonly IReadOnlyDictionary<VoucherStatus, VoucherStatus[]> Voucher =
        new Dictionary<VoucherStatus, VoucherStatus[]>
        {
            [VoucherStatus.Draft] = [VoucherStatus.Pending],
            [VoucherStatus.Pending] = [VoucherStatus.Approved, VoucherStatus.Rejected],
            [VoucherStatus.Approved] = [VoucherStatus.Reconciled],
            [VoucherStatus.Rejected] = [VoucherStatus.Draft],
            [VoucherStatus.Reconciled] = [VoucherStatus.Closed],
            [VoucherStatus.Closed] = []
        };

    public static readonly IReadOnlyDictionary<ExpenseStatus, ExpenseStatus[]> Expense =
        new Dictionary<ExpenseStatus, ExpenseStatus[]>
        {
            [ExpenseStatus.Created] = [ExpenseStatus.InvoiceAttached, ExpenseStatus.PendingApproval],
            [ExpenseStatus.InvoiceAttached] = [ExpenseStatus.PendingApproval],
            [ExpenseStatus.PendingApproval] = [ExpenseStatus.AccountantReview, ExpenseStatus.Rejected],
            [ExpenseStatus.AccountantReview] = [ExpenseStatus.Reconciled, ExpenseStatus.Rejected],
            [ExpenseStatus.Reconciled] = [ExpenseStatus.Closed],
            [ExpenseStatus.Rejected] = [ExpenseStatus.Created],
            [ExpenseStatus.Closed] = []
        };

    public static readonly IReadOnlyDictionary<WorkTaskStatus, WorkTaskStatus[]> WorkTask =
        new Dictionary<WorkTaskStatus, WorkTaskStatus[]>
        {
            [WorkTaskStatus.Todo] = [WorkTaskStatus.InProgress, WorkTaskStatus.Blocked, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.InProgress] = [WorkTaskStatus.Blocked, WorkTaskStatus.Done, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Blocked] = [WorkTaskStatus.InProgress, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Done] = [WorkTaskStatus.InProgress],
            [WorkTaskStatus.Cancelled] = [WorkTaskStatus.Todo]
        };

    public static readonly IReadOnlyDictionary<MeetingStatus, MeetingStatus[]> Meeting =
        new Dictionary<MeetingStatus, MeetingStatus[]>
        {
            [MeetingStatus.Scheduled] = [MeetingStatus.InProgress, MeetingStatus.Cancelled],
            [MeetingStatus.InProgress] = [MeetingStatus.Completed, MeetingStatus.Cancelled],
            [MeetingStatus.Completed] = [],
            [MeetingStatus.Cancelled] = [MeetingStatus.Scheduled]
        };

    public static readonly IReadOnlyDictionary<MotionStatus, MotionStatus[]> Motion =
        new Dictionary<MotionStatus, MotionStatus[]>
        {
            [MotionStatus.Draft] = [MotionStatus.Open, MotionStatus.Withdrawn],
            [MotionStatus.Open] = [MotionStatus.Passed, MotionStatus.Failed, MotionStatus.Withdrawn],
            [MotionStatus.Passed] = [],
            [MotionStatus.Failed] = [],
            [MotionStatus.Withdrawn] = []
        };

    public static readonly IReadOnlyDictionary<EventStatus, EventStatus[]> Event =
        new Dictionary<EventStatus, EventStatus[]>
        {
            [EventStatus.Planned] = [EventStatus.Dispatched, EventStatus.Cancelled],
            [EventStatus.Dispatched] = [EventStatus.Ongoing, EventStatus.Cancelled],
            [EventStatus.Ongoing] = [EventStatus.Completed, EventStatus.Cancelled],
            [EventStatus.Completed] = [],
            [EventStatus.Cancelled] = [EventStatus.Planned]
        };

    public static bool CanTransition<TStatus>(
        IReadOnlyDictionary<TStatus, TStatus[]> map, TStatus from, TStatus to)
        where TStatus : notnull
        => map.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static TStatus[] Next<TStatus>(
        IReadOnlyDictionary<TStatus, TStatus[]> map, TStatus from)
        where TStatus : notnull
        => map.TryGetValue(from, out var allowed) ? allowed : [];
}
