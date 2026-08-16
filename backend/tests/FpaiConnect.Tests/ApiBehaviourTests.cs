using FluentAssertions;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using System.Net;
using System.Net.Http.Json;

namespace FpaiConnect.Tests;

public class AuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_succeeds_with_the_seeded_credentials()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin, password = ApiFactory.Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);

        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.RefreshToken.Should().NotBeNullOrWhiteSpace();
        auth.User.Roles.Should().Contain(RoleNames.SuperAdmin);
        auth.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_fails_with_a_wrong_password()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin, password = "wrong-password-entirely" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_account_is_indistinguishable_from_a_wrong_password()
    {
        var unknown = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@example.com", password = ApiFactory.Password });
        var wrongPassword = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin, password = "wrong-password-entirely" });

        // Same status and same wording, so the endpoint cannot be used to enumerate accounts.
        // The bodies differ only by traceId, which is per-request by design.
        unknown.StatusCode.Should().Be(wrongPassword.StatusCode);

        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ProblemShape>(ApiFactory.Json);
        var wrongProblem = await wrongPassword.Content.ReadFromJsonAsync<ProblemShape>(ApiFactory.Json);

        unknownProblem!.Title.Should().Be(wrongProblem!.Title);
        unknownProblem.Detail.Should().Be(wrongProblem.Detail);
    }

    private sealed record ProblemShape(string? Title, string? Detail, int? Status);

    [Fact]
    public async Task Login_is_case_insensitive_on_the_email()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin.ToUpperInvariant(), password = ApiFactory.Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_rejects_a_malformed_email()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = "not-an-email", password = ApiFactory.Password });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_refresh_token_can_be_exchanged_and_is_then_single_use()
    {
        var client = factory.AnonymousClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin, password = ApiFactory.Password });
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);

        var first = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = auth!.RefreshToken });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Rotation means the original token must not work a second time.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = auth.RefreshToken });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_refused()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = "made-up-token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", ApiFactory.Json);

        me!.Email.Should().Be(Accounts.WelfareHead);
        me.Roles.Should().Contain(RoleNames.DepartmentHead);
        me.DepartmentCode.Should().Be(DepartmentCodes.Welfare);
    }

    [Fact]
    public async Task Google_sign_in_reports_that_it_is_not_configured()
    {
        // No client id is set in the test host, so the endpoint must say so rather than fail obscurely.
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/google",
            new { credential = "irrelevant-because-unconfigured" });

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }
}

public class WelfareEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record CaseCreated(Guid Id, string CaseNumber, string Status);

    [Fact]
    public async Task A_case_can_be_created_and_read_back()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var playerId = await factory.FirstPlayerIdAsync();

        var response = await client.PostAsJsonAsync("/api/welfare/cases", new
        {
            title = "Unpaid match fees", playerId, category = "Salary", priority = "High",
            description = "Reported through the helpline.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CaseCreated>(ApiFactory.Json);
        created!.CaseNumber.Should().MatchRegex(@"^WEL/\d{4}/\d{3}$");
        created.Status.Should().Be("New");

        var fetched = await client.GetAsync($"/api/welfare/cases/{created.Id}");
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Creating_a_case_without_a_title_is_rejected()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var playerId = await factory.FirstPlayerIdAsync();

        var response = await client.PostAsJsonAsync("/api/welfare/cases",
            new { title = "", playerId, category = "Salary" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Creating_a_case_for_an_unknown_member_is_rejected()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);

        var response = await client.PostAsJsonAsync("/api/welfare/cases",
            new { title = "Ghost case", playerId = Guid.NewGuid(), category = "Salary" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_illegal_transition_is_refused_with_a_conflict()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var playerId = await factory.FirstPlayerIdAsync();

        var created = await client.PostAsJsonAsync("/api/welfare/cases",
            new { title = "Transition probe", playerId, category = "Medical" });
        var id = (await created.Content.ReadFromJsonAsync<CaseCreated>(ApiFactory.Json))!.Id;

        var response = await client.PostAsJsonAsync($"/api/welfare/cases/{id}/status",
            new { status = "Resolved" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unknown_status_value_is_rejected()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var playerId = await factory.FirstPlayerIdAsync();

        var created = await client.PostAsJsonAsync("/api/welfare/cases",
            new { title = "Bad status probe", playerId, category = "Medical" });
        var id = (await created.Content.ReadFromJsonAsync<CaseCreated>(ApiFactory.Json))!.Id;

        var response = await client.PostAsJsonAsync($"/api/welfare/cases/{id}/status",
            new { status = "Teleported" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_legal_transition_moves_the_case_and_writes_a_timeline_entry()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var playerId = await factory.FirstPlayerIdAsync();

        var created = await client.PostAsJsonAsync("/api/welfare/cases",
            new { title = "Timeline probe", playerId, category = "Medical" });
        var id = (await created.Content.ReadFromJsonAsync<CaseCreated>(ApiFactory.Json))!.Id;

        var moved = await client.PostAsJsonAsync($"/api/welfare/cases/{id}/status",
            new { status = "UnderReview", comment = "Triaged by the welfare desk." });
        moved.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<WelfareCaseDetailDto>(
            $"/api/welfare/cases/{id}", ApiFactory.Json);

        detail!.Status.Should().Be(Domain.Enums.WelfareStatus.UnderReview);
        detail.Notes.Should().Contain(n => n.Note.Contains("Triaged by the welfare desk."));
    }

    [Fact]
    public async Task A_missing_case_returns_not_found()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var response = await client.GetAsync($"/api/welfare/cases/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Paging_is_clamped_and_reported_accurately()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);

        var page = await client.GetFromJsonAsync<PagedResultDto>(
            "/api/welfare/cases?page=1&pageSize=5", ApiFactory.Json);

        page!.PageSize.Should().Be(5);
        page.Items.Should().HaveCountLessThanOrEqualTo(5);
        page.TotalCount.Should().BeGreaterThan(0);

        // An absurd page size is capped rather than honoured.
        var huge = await client.GetFromJsonAsync<PagedResultDto>(
            "/api/welfare/cases?pageSize=100000", ApiFactory.Json);
        huge!.PageSize.Should().BeLessThanOrEqualTo(200);
    }

    private sealed record PagedResultDto(
        List<System.Text.Json.JsonElement> Items, int Page, int PageSize, int TotalCount);
}

public class FinanceEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record VoucherCreated(Guid Id, string VoucherNumber, string Status, decimal TotalAmount);

    [Fact]
    public async Task A_voucher_totals_amount_plus_tax()
    {
        var client = await factory.ClientForAsync(Accounts.FinanceHead);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var response = await client.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 50_000m, taxAmount = 9_000m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var voucher = await response.Content.ReadFromJsonAsync<VoucherCreated>(ApiFactory.Json);
        voucher!.TotalAmount.Should().Be(59_000m);
        voucher.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task A_voucher_with_a_zero_amount_is_rejected()
    {
        var client = await factory.ClientForAsync(Accounts.FinanceHead);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var response = await client.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 0m, taxAmount = 0m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rejecting_a_voucher_without_a_reason_is_refused()
    {
        var client = await factory.ClientForAsync(Accounts.FinanceHead);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var created = await client.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 1_000m, taxAmount = 0m });
        var id = (await created.Content.ReadFromJsonAsync<VoucherCreated>(ApiFactory.Json))!.Id;

        await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Pending" });

        var response = await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status",
            new { status = "Rejected" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_department_head_cannot_reconcile_a_voucher()
    {
        var client = await factory.ClientForAsync(Accounts.FinanceHead);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var created = await client.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 2_000m, taxAmount = 0m });
        var id = (await created.Content.ReadFromJsonAsync<VoucherCreated>(ApiFactory.Json))!.Id;

        await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Pending" });
        await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Approved" });

        // Reconciliation belongs to the external accountant, not the approver.
        var response = await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status",
            new { status = "Reconciled" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_accountant_can_reconcile_an_approved_voucher()
    {
        var head = await factory.ClientForAsync(Accounts.FinanceHead);
        var accountant = await factory.ClientForAsync(Accounts.Accountant);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var created = await head.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 3_000m, taxAmount = 0m });
        var id = (await created.Content.ReadFromJsonAsync<VoucherCreated>(ApiFactory.Json))!.Id;

        await head.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Pending" });
        await head.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Approved" });

        var response = await accountant.PostAsJsonAsync($"/api/finance/vouchers/{id}/status",
            new { status = "Reconciled" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_open_query_blocks_reconciliation()
    {
        var head = await factory.ClientForAsync(Accounts.FinanceHead);
        var accountant = await factory.ClientForAsync(Accounts.Accountant);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var created = await head.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 4_000m, taxAmount = 0m });
        var id = (await created.Content.ReadFromJsonAsync<VoucherCreated>(ApiFactory.Json))!.Id;

        await head.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Pending" });
        await head.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Approved" });

        var query = await accountant.PostAsJsonAsync("/api/finance/queries",
            new { voucherId = id, question = "Please share the GST invoice." });
        query.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await accountant.PostAsJsonAsync($"/api/finance/vouchers/{id}/status",
            new { status = "Reconciled" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_approved_voucher_can_no_longer_be_edited()
    {
        var client = await factory.ClientForAsync(Accounts.FinanceHead);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var created = await client.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 5_000m, taxAmount = 0m });
        var id = (await created.Content.ReadFromJsonAsync<VoucherCreated>(ApiFactory.Json))!.Id;

        await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Pending" });
        await client.PostAsJsonAsync($"/api/finance/vouchers/{id}/status", new { status = "Approved" });

        var response = await client.PutAsJsonAsync($"/api/finance/vouchers/{id}",
            new { vendorId = vendor, departmentId = department, amount = 999_999m, taxAmount = 0m });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_query_must_reference_exactly_one_record()
    {
        var accountant = await factory.ClientForAsync(Accounts.Accountant);

        var neither = await accountant.PostAsJsonAsync("/api/finance/queries",
            new { question = "About what, exactly?" });
        neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var both = await accountant.PostAsJsonAsync("/api/finance/queries",
            new { voucherId = Guid.NewGuid(), expenseId = Guid.NewGuid(), question = "Both?" });
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class UserManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_user_can_be_created_with_a_role_and_department()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Operations);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            fullName = "Test Coordinator",
            email = $"test.user.{Guid.CreateVersion7():N}@fpai.in",
            role = RoleNames.Staff,
            departmentId = department,
            password = "Str0ng!Passw0rd",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_non_admin_role_requires_a_department()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            fullName = "Departmentless Staffer",
            email = $"nodept.{Guid.CreateVersion7():N}@fpai.in",
            role = RoleNames.Staff,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_duplicate_email_is_refused()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Operations);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            fullName = "Clash", email = Accounts.WelfareHead,
            role = RoleNames.Staff, departmentId = department,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Operations);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            fullName = "Wizard", email = $"wizard.{Guid.CreateVersion7():N}@fpai.in",
            role = "Wizard", departmentId = department,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_weak_password_is_refused()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Operations);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            fullName = "Weak", email = $"weak.{Guid.CreateVersion7():N}@fpai.in",
            role = RoleNames.Staff, departmentId = department, password = "password12",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class SystemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_is_anonymous_and_reports_ok()
    {
        var response = await factory.AnonymousClient().GetAsync("/api/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ok");
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var response = await factory.AnonymousClient().GetAsync("/api/health");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task The_dashboard_returns_a_full_six_month_trend()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard", ApiFactory.Json);

        dashboard!.FinanceTrend.Should().HaveCount(6);
        dashboard.FinanceTrend.Should().OnlyContain(p => p.Income >= 0 && p.Expense >= 0);
        dashboard.WelfareByStatus.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_report_exports_as_csv()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var response = await client.GetAsync("/api/reports/welfare-summary/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Welfare Casework Summary");
    }

    [Fact]
    public async Task An_unknown_report_returns_not_found()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var response = await client.GetAsync("/api/reports/no-such-report");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
