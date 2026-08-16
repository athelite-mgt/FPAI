using FluentAssertions;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using System.Net;
using System.Net.Http.Json;

namespace FpaiConnect.Tests;

public class PreferencesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_new_account_starts_on_the_defaults()
    {
        var client = await factory.ClientForAsync(Accounts.WelfareStaff);
        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", ApiFactory.Json);

        me!.Preferences.ThemeMode.Should().Be("System");
        me.Preferences.ColorScheme.Should().Be("pitch");
        me.Preferences.FontChoice.Should().Be("sans");
    }

    [Fact]
    public async Task Preferences_are_saved_and_survive_a_fresh_sign_in()
    {
        var client = await factory.ClientForAsync(Accounts.LegalHead);

        var saved = await client.PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Dark", colorScheme = "violet", fontChoice = "serif" });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        // A completely new session must see them: they live on the account, not the browser.
        var fresh = await factory.ClientForAsync(Accounts.LegalHead);
        var me = await fresh.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", ApiFactory.Json);

        me!.Preferences.ThemeMode.Should().Be("Dark");
        me.Preferences.ColorScheme.Should().Be("violet");
        me.Preferences.FontChoice.Should().Be("serif");
    }

    [Fact]
    public async Task Preferences_are_returned_by_the_login_response_so_there_is_no_flash()
    {
        var client = await factory.ClientForAsync(Accounts.FinanceHead);
        await client.PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Light", colorScheme = "royal", fontChoice = "mono" });

        var login = await factory.AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { email = Accounts.FinanceHead, password = ApiFactory.Password });
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json);

        auth!.User.Preferences.ColorScheme.Should().Be("royal");
        auth.User.Preferences.FontChoice.Should().Be("mono");
    }

    [Fact]
    public async Task An_unknown_theme_mode_is_refused()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);
        var response = await client.PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Rainbow", colorScheme = "pitch", fontChoice = "sans" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_empty_scheme_or_font_is_refused()
    {
        var client = await factory.ClientForAsync(Accounts.Admin);

        var response = await client.PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Dark", colorScheme = "", fontChoice = "sans" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preferences_require_a_signed_in_user()
    {
        var response = await factory.AnonymousClient().PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Dark", colorScheme = "pitch", fontChoice = "sans" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_users_preferences_do_not_affect_another()
    {
        var legal = await factory.ClientForAsync(Accounts.LegalHead);
        await legal.PutAsJsonAsync("/api/auth/me/preferences",
            new { themeMode = "Dark", colorScheme = "crimson", fontChoice = "legible" });

        var other = await factory.ClientForAsync(Accounts.WelfareStaff);
        var me = await other.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", ApiFactory.Json);

        me!.Preferences.ColorScheme.Should().NotBe("crimson");
    }
}

public class DepartmentManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Created(Guid Id, string Code, string Name);

    [Fact]
    public async Task An_administrator_can_create_a_department()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var code = $"MEDIA{Random.Shared.Next(1000, 9999)}";

        var response = await admin.PostAsJsonAsync("/api/departments",
            new { code, name = "Media & Communications", description = "Press and broadcast." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Created>(ApiFactory.Json);
        created!.Code.Should().Be(code.ToUpperInvariant());
    }

    [Fact]
    public async Task A_duplicate_code_is_refused()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);

        var response = await admin.PostAsJsonAsync("/api/departments",
            new { code = DepartmentCodes.Legal, name = "Another Legal" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_invalid_code_is_refused()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);

        var response = await admin.PostAsJsonAsync("/api/departments",
            new { code = "has spaces!", name = "Bad Code" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_built_in_department_keeps_its_code_but_can_be_renamed()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var legalId = await factory.DepartmentIdAsync(DepartmentCodes.Legal);

        var renameCode = await admin.PutAsJsonAsync($"/api/departments/{legalId}",
            new { code = "LEGAL2", name = "Legal Affairs" });
        renameCode.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var renameName = await admin.PutAsJsonAsync($"/api/departments/{legalId}",
            new { code = DepartmentCodes.Legal, name = "Legal & Disputes", description = "Updated." });
        renameName.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_built_in_department_cannot_be_deleted()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var welfareId = await factory.DepartmentIdAsync(DepartmentCodes.Welfare);

        var response = await admin.DeleteAsync($"/api/departments/{welfareId}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_empty_custom_department_can_be_deleted()
    {
        var admin = await factory.ClientForAsync(Accounts.Admin);
        var code = $"TEMP{Random.Shared.Next(1000, 9999)}";

        var created = await admin.PostAsJsonAsync("/api/departments",
            new { code, name = "Temporary" });
        var department = await created.Content.ReadFromJsonAsync<Created>(ApiFactory.Json);

        var deleted = await admin.DeleteAsync($"/api/departments/{department!.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_department_head_cannot_manage_departments()
    {
        var head = await factory.ClientForAsync(Accounts.WelfareHead);

        var response = await head.PostAsJsonAsync("/api/departments",
            new { code = "SNEAKY", name = "Unauthorised" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Everyone_signed_in_can_still_read_the_department_list()
    {
        var staff = await factory.ClientForAsync(Accounts.WelfareStaff);
        var response = await staff.GetAsync("/api/departments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
