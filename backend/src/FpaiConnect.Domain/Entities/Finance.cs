using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

/// <summary>Payment voucher. Draft -> Pending -> Approved -> Reconciled -> Closed (or Rejected).</summary>
public class Voucher : BaseEntity, IDepartmentScoped
{
    public string VoucherNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    /// <summary>Persisted rather than computed so historic totals survive tax-rule changes.</summary>
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "INR";

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;
    public DateTime VoucherDate { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }

    public Guid? ApprovedById { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }

    public Guid? ReconciledById { get; set; }
    public AppUser? ReconciledBy { get; set; }
    public DateTime? ReconciledAt { get; set; }

    public ICollection<AccountantQuery> Queries { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}

/// <summary>Expense claim. Created -> InvoiceAttached -> PendingApproval -> AccountantReview -> Reconciled -> Closed.</summary>
public class Expense : BaseEntity, IDepartmentScoped
{
    public string ExpenseNumber { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Created;
    public DateTime IncurredOn { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }

    public Guid? SubmittedById { get; set; }
    public AppUser? SubmittedBy { get; set; }
    public Guid? ApprovedById { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<AccountantQuery> Queries { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}

public class Invoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public Guid? ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Received;
    public DateTime IssuedOn { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? PaidOn { get; set; }
}

/// <summary>Raised by the External Accountant against a voucher or expense during review.</summary>
public class AccountantQuery : BaseEntity
{
    public Guid? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public Guid? ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public string Question { get; set; } = string.Empty;
    public string? Response { get; set; }
    public QueryStatus Status { get; set; } = QueryStatus.Open;

    public Guid? RaisedById { get; set; }
    public AppUser? RaisedBy { get; set; }
    public Guid? AnsweredById { get; set; }
    public AppUser? AnsweredBy { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

/// <summary>Monthly income/expense actuals per department. Powers the finance and dashboard trend charts.</summary>
public class LedgerEntry : BaseEntity, IDepartmentScoped
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int FiscalYear { get; set; }
    public int Month { get; set; }
    public LedgerDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal BudgetedAmount { get; set; }
    public string? Notes { get; set; }
}
