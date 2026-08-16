using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace FpaiConnect.Tests;

/// <summary>
/// A fixture with production-like rate limits, so the limiter can be tested honestly rather
/// than tripped over by every other test.
/// </summary>
public class RateLimitedApiFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> Settings()
    {
        foreach (var setting in base.Settings())
        {
            if (setting.Key is "RateLimiting:SignInPerMinutePerClient"
                or "RateLimiting:RegisterPerHourPerClient") continue;
            yield return setting;
        }

        yield return new("RateLimiting:SignInPerMinutePerClient", "5");
        yield return new("RateLimiting:RegisterPerHourPerClient", "3");
    }
}

public class RateLimitingTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Repeated_sign_in_attempts_are_eventually_throttled()
    {
        var client = factory.AnonymousClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "someone@example.com", password = "WrongPassword1!" });
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "a burst of sign-in attempts from one client must be throttled");
    }

    [Fact]
    public async Task Registration_is_held_to_a_tighter_budget_than_sign_in()
    {
        var client = factory.AnonymousClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                fullName = "Bulk Signup",
                email = $"bulk.{Guid.CreateVersion7():N}@example.com",
                password = "Str0ng!Passw0rd",
            });
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
        // The first few must still have been accepted; the limiter throttles, it does not block.
        statuses.Should().Contain(HttpStatusCode.Accepted);
    }
}

/// <summary>
/// Guards the fixture contract itself. A shared database between fixtures silently couples
/// unrelated tests — for instance a lockout test in one class breaking sign-in in another.
/// </summary>
public class FixtureIsolationTests
{
    [Fact]
    public async Task Two_fixtures_never_share_a_database()
    {
        await using var first = new ApiFactory();
        await using var second = new ApiFactory();

        first.DatabasePath.Should().NotBe(second.DatabasePath);

        // Prove it end to end: a change in one must be invisible in the other.
        var firstClient = await first.ClientForAsync(Accounts.Admin);
        await firstClient.PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Dark", colorScheme = "crimson", fontChoice = "legible" });

        var secondClient = await second.ClientForAsync(Accounts.Admin);
        var me = await secondClient.GetFromJsonAsync<Application.Dtos.CurrentUserDto>(
            "/api/auth/me", ApiFactory.Json);

        me!.Preferences.ColorScheme.Should().Be("pitch",
            "each fixture must own an isolated database");
    }

    [Fact]
    public async Task A_fixture_uses_its_own_connection_string()
    {
        await using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();

        db.Database.GetConnectionString().Should().Contain(factory.DatabasePath,
            "the fixture's per-class database must win over appsettings.json");
    }
}
