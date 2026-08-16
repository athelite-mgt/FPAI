using FpaiConnect.Application.Common;
using FpaiConnect.Domain.Entities;

namespace FpaiConnect.Api.Security;

public static class AuthorizationSetup
{
    private const string Admin = RoleNames.SuperAdmin;
    private const string Head = RoleNames.DepartmentHead;
    private const string Staff = RoleNames.Staff;
    private const string Accountant = RoleNames.ExternalAccountant;

    /// <summary>
    /// Module-level gates. Row-level department scoping is applied inside each handler
    /// through ICurrentUser, because it depends on the record being touched.
    /// </summary>
    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Welfare — the external accountant has no business here.
            .AddPolicy(Policies.WelfareRead, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.WelfareWrite, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.WelfareApprove, p => p.RequireRole(Admin, Head))

            .AddPolicy(Policies.LegalRead, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.LegalWrite, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.LegalApprove, p => p.RequireRole(Admin, Head))

            // Finance — the only module the external accountant can reach.
            .AddPolicy(Policies.FinanceRead, p => p.RequireRole(Admin, Head, Staff, Accountant))
            .AddPolicy(Policies.FinanceWrite, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.FinanceApprove, p => p.RequireRole(Admin, Head))
            .AddPolicy(Policies.FinanceReconcile, p => p.RequireRole(Admin, Accountant))

            .AddPolicy(Policies.GovernanceRead, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.GovernanceWrite, p => p.RequireRole(Admin, Head))
            .AddPolicy(Policies.GovernanceApprove, p => p.RequireRole(Admin, Head))

            .AddPolicy(Policies.OperationsRead, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.OperationsWrite, p => p.RequireRole(Admin, Head, Staff))

            .AddPolicy(Policies.DocumentsRead, p => p.RequireRole(Admin, Head, Staff, Accountant))
            .AddPolicy(Policies.DocumentsWrite, p => p.RequireRole(Admin, Head, Staff))

            .AddPolicy(Policies.TasksRead, p => p.RequireRole(Admin, Head, Staff, Accountant))
            .AddPolicy(Policies.TasksWrite, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.TasksApprove, p => p.RequireRole(Admin, Head))

            .AddPolicy(Policies.ReportsRead, p => p.RequireRole(Admin, Head, Accountant))

            .AddPolicy(Policies.UsersRead, p => p.RequireRole(Admin, Head))
            .AddPolicy(Policies.UsersManage, p => p.RequireRole(Admin))

            .AddPolicy(Policies.DirectoryRead, p => p.RequireRole(Admin, Head, Staff))
            .AddPolicy(Policies.DirectoryWrite, p => p.RequireRole(Admin, Head));

        return services;
    }
}
