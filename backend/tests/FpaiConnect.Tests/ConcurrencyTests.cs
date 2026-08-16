using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace FpaiConnect.Tests;

/// <summary>
/// Proves the application itself is safe when the same account signs in many times at once —
/// the case that previously surfaced a DbUpdateConcurrencyException from the login handler.
/// </summary>
public class ConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Twenty_concurrent_logins_for_one_account_all_succeed()
    {
        var client = factory.AnonymousClient();

        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = Accounts.Admin, password = ApiFactory.Password });
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        });

        var results = await Task.WhenAll(attempts);

        var failures = results.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        failures.Should().BeEmpty(
            "concurrent sign-ins must not race: " +
            string.Join(" | ", failures.Select(f => $"{(int)f.StatusCode} {f.Body}")));
    }

    [Fact]
    public async Task Concurrent_reads_across_modules_all_succeed()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);

        var paths = new[]
        {
            "/api/dashboard", "/api/welfare/cases", "/api/legal/cases", "/api/finance/vouchers",
            "/api/meetings", "/api/events", "/api/documents", "/api/tasks", "/api/approvals",
        };

        var responses = await Task.WhenAll(paths.Select(async path =>
            (path, response: await client.GetAsync(path))));

        foreach (var (path, response) in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} under concurrent load");
        }
    }

    [Fact]
    public async Task A_failed_login_does_not_disturb_a_concurrent_successful_one()
    {
        var client = factory.AnonymousClient();

        var good = Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/api/auth/login",
                new { email = Accounts.WelfareStaff, password = ApiFactory.Password }));

        // Deliberately fewer than the five-attempt lockout threshold, so this test measures
        // interference between concurrent sign-ins rather than the lockout policy itself
        // (which LockoutTests covers).
        var bad = Enumerable.Range(0, 3).Select(_ =>
            client.PostAsJsonAsync("/api/auth/login",
                new { email = Accounts.WelfareStaff, password = "wrong" }));

        var all = await Task.WhenAll(good.Concat(bad));

        all.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(5);
        all.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(3);
        all.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError);
    }
}

/// <summary>
/// Brute-force protection. Uses a throwaway account so locking it out cannot affect
/// any other test in the suite.
/// </summary>
public class LockoutTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Five_failed_attempts_lock_the_account_and_the_right_password_then_fails()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var department = await factory.DepartmentIdAsync(Domain.Entities.DepartmentCodes.Operations);

        var email = $"lockout.probe.{Guid.CreateVersion7():N}@fpai.in";
        const string password = "Str0ng!Passw0rd";

        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Lockout Probe", email, role = Domain.Entities.RoleNames.Staff,
            departmentId = department, password,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var client = factory.AnonymousClient();

        // The account works before any failures.
        var before = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login",
                new { email, password = "wrong-password" });
            failed.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Locked);
        }

        // Even the correct password is refused once the account is locked.
        var after = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        after.StatusCode.Should().Be(HttpStatusCode.Locked);
    }
}
