using FpaiConnect.Application.Common;
using FpaiConnect.Domain.Enums;
using FluentAssertions;

namespace FpaiConnect.Tests;

/// <summary>Pure unit tests over the workflow state machines — no host, no database.</summary>
public class WorkflowRulesTests
{
    [Theory]
    [InlineData(WelfareStatus.New, WelfareStatus.UnderReview, true)]
    [InlineData(WelfareStatus.New, WelfareStatus.Assigned, true)]
    [InlineData(WelfareStatus.New, WelfareStatus.Resolved, false)]   // cannot skip the workflow
    [InlineData(WelfareStatus.New, WelfareStatus.Closed, false)]
    [InlineData(WelfareStatus.InProgress, WelfareStatus.Resolved, true)]
    [InlineData(WelfareStatus.Resolved, WelfareStatus.Closed, true)]
    [InlineData(WelfareStatus.Closed, WelfareStatus.InProgress, true)] // reopening is allowed
    [InlineData(WelfareStatus.Closed, WelfareStatus.New, false)]
    public void Welfare_transitions_follow_the_state_machine(
        WelfareStatus from, WelfareStatus to, bool allowed)
    {
        WorkflowRules.CanTransition(WorkflowRules.Welfare, from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData(LegalStatus.Registered, LegalStatus.Filed, true)]
    [InlineData(LegalStatus.Registered, LegalStatus.Closed, false)]
    [InlineData(LegalStatus.Filed, LegalStatus.HearingScheduled, true)]
    [InlineData(LegalStatus.DecisionReceived, LegalStatus.Closed, true)]
    [InlineData(LegalStatus.Closed, LegalStatus.Filed, false)]        // closed is terminal
    public void Legal_transitions_follow_the_state_machine(
        LegalStatus from, LegalStatus to, bool allowed)
    {
        WorkflowRules.CanTransition(WorkflowRules.Legal, from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData(VoucherStatus.Draft, VoucherStatus.Pending, true)]
    [InlineData(VoucherStatus.Draft, VoucherStatus.Approved, false)]  // must be submitted first
    [InlineData(VoucherStatus.Pending, VoucherStatus.Approved, true)]
    [InlineData(VoucherStatus.Pending, VoucherStatus.Rejected, true)]
    [InlineData(VoucherStatus.Approved, VoucherStatus.Reconciled, true)]
    [InlineData(VoucherStatus.Rejected, VoucherStatus.Draft, true)]   // corrections reopen the draft
    [InlineData(VoucherStatus.Closed, VoucherStatus.Draft, false)]
    public void Voucher_transitions_follow_the_state_machine(
        VoucherStatus from, VoucherStatus to, bool allowed)
    {
        WorkflowRules.CanTransition(WorkflowRules.Voucher, from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData(ExpenseStatus.Created, ExpenseStatus.PendingApproval, true)]
    [InlineData(ExpenseStatus.Created, ExpenseStatus.Reconciled, false)]
    [InlineData(ExpenseStatus.PendingApproval, ExpenseStatus.AccountantReview, true)]
    [InlineData(ExpenseStatus.AccountantReview, ExpenseStatus.Reconciled, true)]
    [InlineData(ExpenseStatus.Closed, ExpenseStatus.Created, false)]
    public void Expense_transitions_follow_the_state_machine(
        ExpenseStatus from, ExpenseStatus to, bool allowed)
    {
        WorkflowRules.CanTransition(WorkflowRules.Expense, from, to).Should().Be(allowed);
    }

    [Fact]
    public void Every_welfare_status_has_an_entry_so_Next_never_throws()
    {
        foreach (var status in Enum.GetValues<WelfareStatus>())
        {
            WorkflowRules.Next(WorkflowRules.Welfare, status).Should().NotBeNull();
        }
    }

    [Fact]
    public void Terminal_statuses_offer_no_onward_transition()
    {
        WorkflowRules.Next(WorkflowRules.Legal, LegalStatus.Closed).Should().BeEmpty();
        WorkflowRules.Next(WorkflowRules.Voucher, VoucherStatus.Closed).Should().BeEmpty();
        WorkflowRules.Next(WorkflowRules.Motion, MotionStatus.Passed).Should().BeEmpty();
        WorkflowRules.Next(WorkflowRules.Meeting, MeetingStatus.Completed).Should().BeEmpty();
    }

    [Fact]
    public void No_workflow_lets_a_status_transition_to_itself()
    {
        foreach (var status in Enum.GetValues<WelfareStatus>())
        {
            WorkflowRules.CanTransition(WorkflowRules.Welfare, status, status)
                .Should().BeFalse($"{status} should not transition to itself");
        }
    }
}

public class PageQueryTests
{
    [Fact]
    public void Page_below_one_is_clamped()
    {
        new PageQuery { Page = 0 }.Page.Should().Be(1);
        new PageQuery { Page = -5 }.Page.Should().Be(1);
    }

    [Fact]
    public void PageSize_is_capped_so_a_client_cannot_request_the_whole_table()
    {
        new PageQuery { PageSize = 10_000 }.PageSize.Should().Be(PageQuery.MaxPageSize);
    }

    [Fact]
    public void A_nonsensical_PageSize_falls_back_to_the_default()
    {
        new PageQuery { PageSize = 0 }.PageSize.Should().Be(PageQuery.DefaultPageSize);
        new PageQuery { PageSize = -1 }.PageSize.Should().Be(PageQuery.DefaultPageSize);
    }

    [Fact]
    public void A_sensible_PageSize_is_kept()
    {
        new PageQuery { PageSize = 50 }.PageSize.Should().Be(50);
    }
}
