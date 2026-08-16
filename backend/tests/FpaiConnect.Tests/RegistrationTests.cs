using FluentAssertions;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using System.Net;
using System.Net.Http.Json;

namespace FpaiConnect.Tests;

/// <summary>
/// Self-registration is open, so the approval gate is the only thing standing between a
/// stranger and the association's data. These tests hold that gate shut.
/// </summary>
public class RegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object NewApplicant(out string email, string? note = null)
    {
        email = $"applicant.{Guid.CreateVersion7():N}@example.com";
        return new
        {
            fullName = "Ravi Deshmukh",
            email,
            password = "Str0ng!Passw0rd",
            jobTitle = "Player Agent",
            note,
        };
    }

    [Fact]
    public async Task Anyone_can_request_an_account()
    {
        var response = await factory.AnonymousClient()
            .PostAsJsonAsync("/api/auth/register", NewApplicant(out _));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<RegistrationResultDto>(ApiFactory.Json);
        result!.Status.Should().Be("PendingApproval");
    }

    [Fact]
    public async Task A_pending_account_is_refused_a_token_even_with_the_right_password()
    {
        var client = factory.AnonymousClient();
        await client.PostAsJsonAsync("/api/auth/register", NewApplicant(out var email));

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Str0ng!Passw0rd" });

        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("PendingApproval");
        // The decisive assertion: no credential of any kind is handed out.
        body.Should().NotContain("accessToken");
        body.Should().NotContain("refreshToken");
    }

    [Fact]
    public async Task Registering_an_existing_address_does_not_reveal_that_it_exists()
    {
        var client = factory.AnonymousClient();

        var known = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Impostor", email = Accounts.Admin, password = "Str0ng!Passw0rd",
        });
        var unknown = await client.PostAsJsonAsync("/api/auth/register", NewApplicant(out _));

        known.StatusCode.Should().Be(unknown.StatusCode);
        known.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var knownBody = await known.Content.ReadFromJsonAsync<RegistrationResultDto>(ApiFactory.Json);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<RegistrationResultDto>(ApiFactory.Json);
        knownBody!.Message.Should().Be(unknownBody!.Message);
    }

    [Fact]
    public async Task Registering_does_not_change_an_existing_account()
    {
        var client = factory.AnonymousClient();

        await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Impostor", email = Accounts.Admin, password = "Attacker!Passw0rd",
        });

        // The real administrator's password and access must be untouched.
        var attacker = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin, password = "Attacker!Passw0rd" });
        attacker.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var genuine = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.Admin, password = ApiFactory.Password });
        genuine.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_weak_password_is_refused_at_registration()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Weak Applicant",
            email = $"weak.{Guid.CreateVersion7():N}@example.com",
            password = "password12",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_malformed_email_is_refused_at_registration()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Bad Address", email = "not-an-email", password = "Str0ng!Passw0rd",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_pending_account_appears_in_the_administrator_queue()
    {
        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/register",
            NewApplicant(out var email, "I represent three FPAI members."));

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);

        var applicant = pending!.SingleOrDefault(p => p.Email == email);
        applicant.Should().NotBeNull();
        applicant!.RegistrationNote.Should().Be("I represent three FPAI members.");
        applicant.SignedUpWithGoogle.Should().BeFalse();
    }

    [Fact]
    public async Task Only_an_administrator_can_see_the_queue()
    {
        var head = await factory.ClientForAsync(Accounts.WelfareHead);
        var staff = await factory.ClientForAsync(Accounts.WelfareStaff);

        (await head.GetAsync("/api/users/pending")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.GetAsync("/api/users/pending")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approval_grants_exactly_the_chosen_role_and_department()
    {
        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", NewApplicant(out var email));

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);
        var welfare = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);

        var approve = await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/approve",
            new { role = RoleNames.Staff, departmentId = welfare, note = "Verified." });
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        // They can now sign in, with precisely the role that was granted.
        var login = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Str0ng!Passw0rd" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);
        auth!.User.Roles.Should().BeEquivalentTo([RoleNames.Staff]);
        auth.User.DepartmentCode.Should().Be(DepartmentCodes.Welfare);
        auth.User.Status.Should().Be("Active");
    }

    [Fact]
    public async Task An_approved_staff_member_is_still_confined_by_their_role()
    {
        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", NewApplicant(out var email));

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);
        var welfare = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);

        await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/approve",
            new { role = RoleNames.Staff, departmentId = welfare });

        var login = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Str0ng!Passw0rd" });
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);

        var client = factory.AnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        (await client.GetAsync("/api/welfare/cases")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/users/pending")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approving_without_a_department_is_refused_for_a_scoped_role()
    {
        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", NewApplicant(out var email));

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);

        var response = await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/approve",
            new { role = RoleNames.Staff });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_rejected_applicant_is_told_why_and_still_gets_no_token()
    {
        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", NewApplicant(out var email));

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);

        var reject = await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/reject",
            new { reason = "This address is not associated with an FPAI member." });
        reject.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var login = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Str0ng!Passw0rd" });

        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("Rejected");
        body.Should().Contain("not associated with an FPAI member");
        body.Should().NotContain("accessToken");
    }

    [Fact]
    public async Task A_rejection_needs_a_reason()
    {
        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", NewApplicant(out var email));

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);

        var response = await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/reject", new { reason = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_active_account_cannot_be_rejected()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var users = await admin.GetFromJsonAsync<PagedUsers>("/api/users?search=welfare.head", ApiFactory.Json);
        var target = users!.Items.Single(u => u.Email == Accounts.WelfareHead);

        var response = await admin.PostAsJsonAsync($"/api/users/{target.Id}/reject",
            new { reason = "Trying to reject an active account." });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed record PagedUsers(List<UserListDto> Items);
}
