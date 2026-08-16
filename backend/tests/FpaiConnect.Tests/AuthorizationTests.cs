using FluentAssertions;
using FpaiConnect.Domain.Entities;
using System.Net;
using System.Net.Http.Json;

namespace FpaiConnect.Tests;

/// <summary>
/// The authorization matrix, exercised against the real HTTP pipeline.
/// These are the tests that matter most: the UI hides things, but only the server enforces them.
/// </summary>
public class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/dashboard")]
    [InlineData("/api/welfare/cases")]
    [InlineData("/api/legal/cases")]
    [InlineData("/api/finance/vouchers")]
    [InlineData("/api/meetings")]
    [InlineData("/api/events")]
    [InlineData("/api/documents")]
    [InlineData("/api/tasks")]
    [InlineData("/api/approvals")]
    [InlineData("/api/users")]
    public async Task Anonymous_requests_are_refused(string path)
    {
        var response = await factory.AnonymousClient().GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_forged_token_is_refused()
    {
        var client = factory.AnonymousClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not.a.real.token");

        var response = await client.GetAsync("/api/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/welfare/cases")]
    [InlineData("/api/legal/cases")]
    [InlineData("/api/finance/vouchers")]
    [InlineData("/api/meetings")]
    [InlineData("/api/events")]
    [InlineData("/api/documents")]
    [InlineData("/api/tasks")]
    [InlineData("/api/approvals")]
    [InlineData("/api/reports")]
    [InlineData("/api/users")]
    [InlineData("/api/users/audit")]
    public async Task A_super_admin_reaches_every_module(string path)
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} should be readable by a super admin");
    }

    [Theory]
    [InlineData("/api/welfare/cases")]
    [InlineData("/api/legal/cases")]
    [InlineData("/api/meetings")]
    [InlineData("/api/events")]
    [InlineData("/api/users")]
    [InlineData("/api/users/audit")]
    public async Task The_external_accountant_is_confined_to_finance(string path)
    {
        var client = await factory.ClientForAsync(Accounts.Accountant);
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{path} is outside finance");
    }

    [Theory]
    [InlineData("/api/finance/vouchers")]
    [InlineData("/api/finance/expenses")]
    [InlineData("/api/finance/queries")]
    [InlineData("/api/finance/summary")]
    [InlineData("/api/documents")]
    public async Task The_external_accountant_reaches_finance(string path)
    {
        var client = await factory.ClientForAsync(Accounts.Accountant);
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Staff_cannot_list_users()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareStaff);
        var response = await client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_cannot_create_a_user()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareStaff);
        var response = await client.PostAsJsonAsync("/api/users",
            new { fullName = "Intruder", email = "intruder@fpai.in", role = "SuperAdmin" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_department_head_cannot_create_records_in_another_department()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var legalDepartment = await factory.DepartmentIdAsync(DepartmentCodes.Legal);

        var response = await client.PostAsJsonAsync("/api/tasks",
            new { title = "Cross-department task", departmentId = legalDepartment });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_department_head_can_create_records_in_their_own_department()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareHead);
        var welfareDepartment = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);

        var response = await client.PostAsJsonAsync("/api/tasks",
            new { title = "Own-department task", departmentId = welfareDepartment });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_department_head_may_read_another_department()
    {
        // Heads have organisation-wide read, which is what makes cross-department oversight work.
        var client = await factory.ClientForAsync(Accounts.LegalHead);
        var response = await client.GetAsync("/api/finance/vouchers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_accountant_cannot_raise_a_voucher()
    {
        var client = await factory.ClientForAsync(Accounts.Accountant);
        var department = await factory.DepartmentIdAsync(DepartmentCodes.Finance);
        var vendor = await factory.FirstVendorIdAsync();

        var response = await client.PostAsJsonAsync("/api/finance/vouchers",
            new { vendorId = vendor, departmentId = department, amount = 1000m, taxAmount = 0m });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_cannot_close_a_welfare_case()
    {
        var head = await factory.ClientForAsync(Accounts.WelfareHead);
        var staff = await factory.ClientForAsync(Accounts.WelfareStaff);
        var playerId = await factory.FirstPlayerIdAsync();

        var created = await head.PostAsJsonAsync("/api/welfare/cases", new
        {
            title = "Closure authority probe", playerId, category = "Medical", priority = "Low",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var caseId = (await created.Content.ReadFromJsonAsync<CaseIdOnly>(ApiFactory.Json))!.Id;

        // Walk it to a state from which Closed is legal.
        await head.PostAsJsonAsync($"/api/welfare/cases/{caseId}/status", new { status = "UnderReview" });

        var attempt = await staff.PostAsJsonAsync($"/api/welfare/cases/{caseId}/status",
            new { status = "Closed" });

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record CaseIdOnly(Guid Id);
}
