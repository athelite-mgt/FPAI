using FpaiConnect.Application.Dtos;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FpaiConnect.Tests;

/// <summary>
/// Boots the real API against a private SQLite file per test class, so tests exercise the
/// genuine pipeline — authentication, authorization policies, EF Core and the seeder —
/// rather than mocks. The file is deleted on dispose.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Fpai@Connect2025!";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"fpai-test-{Guid.CreateVersion7():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting, not ConfigureAppConfiguration.
        //
        // Under minimal hosting the factory's ConfigureAppConfiguration callbacks are applied
        // *before* the application's own appsettings.json, so anything the app also defines —
        // notably ConnectionStrings:Default — wins and every test host silently shares one
        // SQLite file. UseSetting writes into host configuration, which takes precedence, and
        // gives each fixture the isolated database this class promises.
        foreach (var (key, value) in Settings())
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>Overridable so a derived fixture can change individual settings.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string>> Settings()
    {
        yield return new("ConnectionStrings:Default", $"Data Source={_databasePath}");
        yield return new("Database:Provider", "Sqlite");
        yield return new("Jwt:SigningKey", "test-signing-key-that-is-long-enough-for-hmac-sha256");
        yield return new("Seed:Enabled", "true");
        yield return new("Seed:DemoPassword", Password);
        yield return new("Storage:Provider", "Local");
        yield return new("Storage:LocalRoot",
            Path.Combine(Path.GetTempPath(), $"fpai-test-files-{Guid.CreateVersion7():N}"));

        // The suite signs in hundreds of times from one address. Rate limiting is exercised
        // deliberately by RateLimitingTests, not incidentally by every other test.
        yield return new("RateLimiting:SignInPerMinutePerClient", "100000");
        yield return new("RateLimiting:RegisterPerHourPerClient", "100000");
    }

    /// <summary>The database this fixture owns, exposed so tests can assert isolation.</summary>
    public string DatabasePath => _databasePath;

    public async Task InitializeAsync()
    {
        // Force the host to build so migrations and seeding run before the first test.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.CanConnectAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        TryDelete(_databasePath);
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* the file is still held; the temp directory will be cleaned up */ }
    }

    /// <summary>Signs in and returns a client with the bearer token already attached.</summary>
    public async Task<HttpClient> ClientForAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = Password });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Sign-in for {email} failed with {(int)response.StatusCode}: {body}");
        }
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    public HttpClient AnonymousClient() => CreateClient();

    public async Task<Guid> DepartmentIdAsync(string code)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Departments.Where(d => d.Code == code).Select(d => d.Id).FirstAsync();
    }

    public async Task<Guid> FirstPlayerIdAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Players.Select(p => p.Id).FirstAsync();
    }

    public async Task<Guid> FirstVendorIdAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Vendors.Select(v => v.Id).FirstAsync();
    }
}

/// <summary>Well-known seeded accounts, one per role.</summary>
public static class Accounts
{
    public const string Admin = "admin@fpai.in";
    public const string WelfareHead = "welfare.head@fpai.in";
    public const string LegalHead = "legal.head@fpai.in";
    public const string FinanceHead = "finance.head@fpai.in";
    public const string WelfareStaff = "welfare.staff@fpai.in";
    public const string Accountant = "accountant@external-ca.in";
}
