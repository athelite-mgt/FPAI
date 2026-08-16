namespace FpaiConnect.Application.Common;

/// <summary>
/// Authorization is two-dimensional:
///   1. Module gate  - which roles may touch a module at all (these policy names).
///   2. Row scope    - which departments' rows they may see or change (ICurrentUser.Can*Department).
/// Both are enforced server-side; the frontend only mirrors them for presentation.
/// </summary>
public static class Policies
{
    public const string WelfareRead = "Welfare.Read";
    public const string WelfareWrite = "Welfare.Write";
    public const string WelfareApprove = "Welfare.Approve";

    public const string LegalRead = "Legal.Read";
    public const string LegalWrite = "Legal.Write";
    public const string LegalApprove = "Legal.Approve";

    public const string FinanceRead = "Finance.Read";
    public const string FinanceWrite = "Finance.Write";
    public const string FinanceApprove = "Finance.Approve";
    /// <summary>Accountant review and reconciliation. External Accountant or Super Admin.</summary>
    public const string FinanceReconcile = "Finance.Reconcile";

    public const string GovernanceRead = "Governance.Read";
    public const string GovernanceWrite = "Governance.Write";
    public const string GovernanceApprove = "Governance.Approve";

    public const string OperationsRead = "Operations.Read";
    public const string OperationsWrite = "Operations.Write";

    public const string DocumentsRead = "Documents.Read";
    public const string DocumentsWrite = "Documents.Write";

    public const string TasksRead = "Tasks.Read";
    public const string TasksWrite = "Tasks.Write";
    public const string TasksApprove = "Tasks.Approve";

    public const string ReportsRead = "Reports.Read";

    public const string UsersRead = "Users.Read";
    public const string UsersManage = "Users.Manage";

    public const string DirectoryRead = "Directory.Read";
    public const string DirectoryWrite = "Directory.Write";
}
