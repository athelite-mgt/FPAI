using FluentAssertions;
using FpaiConnect.Api.Security;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FpaiConnect.Tests;

/// <summary>
/// Stands in for the real Microsoft signature/issuer/audience verification, which cannot be
/// exercised in a test without a live Entra tenant. To keep every test independent — no
/// shared mutable state, safe under parallel execution — the "ID token" the tests submit is
/// simply the JSON of the identity they want the endpoint to see; this fake parses it back
/// out instead of validating a signature. Everything downstream of validation (the account
/// matching, the pending-approval gate, the token issuance) is the real production code.
/// </summary>
public sealed class FakeMicrosoftIdTokenValidator : IMicrosoftIdTokenValidator
{
    public bool IsConfigured => true;

    public Task<MicrosoftIdentity?> ValidateAsync(string idToken, CancellationToken ct)
    {
        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<MicrosoftIdentity>(idToken));
        }
        catch (JsonException)
        {
            return Task.FromResult<MicrosoftIdentity?>(null);
        }
    }

    public static string TokenFor(string subject, string? email, string? name, string tenantId = "consumers") =>
        JsonSerializer.Serialize(new MicrosoftIdentity(subject, tenantId, email, name));
}

/// <summary>A fixture whose Microsoft validator is the fake above, so the whole flow is testable.</summary>
public class MicrosoftApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IMicrosoftIdTokenValidator, FakeMicrosoftIdTokenValidator>());
    }
}

public class MicrosoftSignInNotConfiguredTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Microsoft_sign_in_reports_that_it_is_not_configured()
    {
        // The default fixture never sets Authentication:Microsoft:ClientId.
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/microsoft",
            new { idToken = "irrelevant" });

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }
}

public class MicrosoftSignInTests(MicrosoftApiFactory factory) : IClassFixture<MicrosoftApiFactory>
{
    [Fact]
    public async Task An_unrecognised_token_is_refused()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/microsoft",
            new { idToken = "not valid json, so the fake cannot parse an identity from it" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_with_no_email_is_refused()
    {
        var token = FakeMicrosoftIdTokenValidator.TokenFor(
            subject: Guid.NewGuid().ToString(), email: null, name: "No Email");

        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/microsoft",
            new { idToken = token });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unknown_Microsoft_account_registers_as_pending_with_no_token()
    {
        var email = $"ms.applicant.{Guid.NewGuid():N}@outlook.com";
        var token = FakeMicrosoftIdTokenValidator.TokenFor(
            subject: Guid.NewGuid().ToString(), email: email, name: "MS Applicant");

        var response = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/microsoft",
            new { idToken = token });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("PendingApproval");
        body.Should().NotContain("accessToken");

        // And it lands in the same queue as an email/password or Google sign-up.
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.SingleOrDefault(p => p.Email == email);
        applicant.Should().NotBeNull();
        applicant!.RegistrationNote.Should().Contain("Microsoft");
    }

    [Fact]
    public async Task Approving_a_Microsoft_applicant_lets_them_sign_in_through_Microsoft_again()
    {
        var email = $"ms.approved.{Guid.NewGuid():N}@outlook.com";
        var subject = Guid.NewGuid().ToString();
        var token = FakeMicrosoftIdTokenValidator.TokenFor(subject, email, "MS Approved");

        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/microsoft", new { idToken = token });

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);
        var welfare = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);

        var approve = await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/approve",
            new { role = RoleNames.Staff, departmentId = welfare });
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        // Signing in again with the *same* Microsoft identity now succeeds.
        var signIn = await anonymous.PostAsJsonAsync("/api/auth/microsoft", new { idToken = token });
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await signIn.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);
        auth!.User.Roles.Should().BeEquivalentTo([RoleNames.Staff]);
        auth.User.Status.Should().Be("Active");
    }

    [Fact]
    public async Task A_second_Microsoft_account_cannot_claim_an_email_already_linked_to_a_different_one()
    {
        var email = $"ms.claimed.{Guid.NewGuid():N}@outlook.com";
        var firstSubject = Guid.NewGuid().ToString();
        var firstToken = FakeMicrosoftIdTokenValidator.TokenFor(firstSubject, email, "First Owner");

        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/microsoft", new { idToken = firstToken });

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);
        var welfare = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);
        await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/approve",
            new { role = RoleNames.Staff, departmentId = welfare });

        // A different Microsoft subject presenting the same email must be refused, not
        // silently take over the account.
        var impostorToken = FakeMicrosoftIdTokenValidator.TokenFor(
            Guid.NewGuid().ToString(), email, "Impostor");
        var impostorAttempt = await anonymous.PostAsJsonAsync("/api/auth/microsoft",
            new { idToken = impostorToken });

        impostorAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await impostorAttempt.Content.ReadAsStringAsync()).Should().Contain("already linked");
    }

    [Fact]
    public async Task An_admin_created_invitation_is_completed_by_first_Microsoft_sign_in()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var welfare = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);
        var email = $"ms.invited.{Guid.NewGuid():N}@outlook.com";

        // Created with no password, exactly like inviting someone to sign in with Google.
        var invited = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Invited By Microsoft", email, role = RoleNames.Staff, departmentId = welfare,
        });
        invited.StatusCode.Should().Be(HttpStatusCode.Created);

        var token = FakeMicrosoftIdTokenValidator.TokenFor(Guid.NewGuid().ToString(), email, "Invited");
        var signIn = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/microsoft",
            new { idToken = token });

        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await signIn.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);
        auth!.User.Status.Should().Be("Active");
        auth.User.Roles.Should().BeEquivalentTo([RoleNames.Staff]);
    }

    [Fact]
    public async Task A_rejected_Microsoft_applicant_is_still_refused_after_a_second_attempt()
    {
        var email = $"ms.rejected.{Guid.NewGuid():N}@outlook.com";
        var token = FakeMicrosoftIdTokenValidator.TokenFor(Guid.NewGuid().ToString(), email, "Rejected");

        var anonymous = factory.AnonymousClient();
        await anonymous.PostAsJsonAsync("/api/auth/microsoft", new { idToken = token });

        var admin = await factory.ClientForAsync(Accounts.Admin);
        var pending = await admin.GetFromJsonAsync<List<PendingUserDto>>("/api/users/pending", ApiFactory.Json);
        var applicant = pending!.Single(p => p.Email == email);
        await admin.PostAsJsonAsync($"/api/users/{applicant.Id}/reject",
            new { reason = "Not an FPAI member." });

        var retry = await anonymous.PostAsJsonAsync("/api/auth/microsoft", new { idToken = token });
        retry.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await retry.Content.ReadAsStringAsync();
        body.Should().Contain("Rejected");
        body.Should().NotContain("accessToken");
    }
}
