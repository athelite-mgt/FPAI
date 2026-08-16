using FpaiConnect.Api.Common;
using FpaiConnect.Api.Security;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FpaiConnect.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Statuses that must never receive a token. A self-registered account sits in
    /// PendingApproval with no role, so even if a token leaked it could read nothing —
    /// but withholding the token entirely is the stronger guarantee.
    /// </summary>
    private static bool CanSignIn(AppUser user) =>
        user is { IsDeleted: false, Status: UserStatus.Active };

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // ------------------------------------------------------------------ register
        group.MapPost("/register", async (
            [FromBody] RegisterRequest request,
            UserManager<AppUser> users,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var email = request.Email.Trim();
            var existing = await users.FindByEmailAsync(email);

            // Never disclose whether an address is already registered: an attacker could
            // otherwise enumerate staff addresses through this anonymous endpoint.
            if (existing is not null)
            {
                return Results.Accepted(value: new RegistrationResultDto(
                    "PendingApproval",
                    "Your request has been received. An administrator will review it shortly.",
                    email));
            }

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = false,
                FullName = request.FullName.Trim(),
                JobTitle = request.JobTitle,
                RegistrationNote = request.Note,
                // No department and no role until an administrator decides both.
                DepartmentId = null,
                Status = UserStatus.PendingApproval,
            };

            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["registration"] = created.Errors.Select(e => e.Description).ToArray(),
                });
            }

            await NotifyAdministratorsAsync(db, user, ct);

            return Results.Accepted(value: new RegistrationResultDto(
                "PendingApproval",
                "Your request has been received. An administrator will review it shortly.",
                email));
        })
        .AllowAnonymous()
        .RequireRateLimiting("register")
        .WithName("Register")
        .WithSummary("Request an account. The account cannot be used until an admin approves it.");

        // ------------------------------------------------------------------ login
        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            JwtTokenService tokens,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var user = await users.FindByEmailAsync(request.Email);
            // Same response whether the account is missing, deleted or the password is wrong,
            // so the endpoint cannot be used to enumerate valid addresses.
            if (user is null || user.IsDeleted)
                return Results.Problem(title: "Invalid credentials",
                    detail: "Email or password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);

            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (result.IsLockedOut)
                return Results.Problem(title: "Account locked",
                    detail: "Too many failed attempts. Try again in 15 minutes.",
                    statusCode: StatusCodes.Status423Locked);

            if (!result.Succeeded)
                return Results.Problem(title: "Invalid credentials",
                    detail: "Email or password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);

            // The password was right, so it is safe to explain why they still cannot get in.
            if (!CanSignIn(user)) return PendingOrBlocked(user);

            // Stamp the sign-in with a direct UPDATE and leave the tracked entity untouched.
            // SignInManager rotates the user's ConcurrencyStamp when it resets the lockout
            // counter, so any pending modification to this entity would be flushed later by
            // IssueAsync with a stale stamp and turn a successful login into a 500.
            var signedInAt = DateTime.UtcNow;
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, signedInAt), ct);

            var pair = await tokens.IssueAsync(user, ct);
            return Results.Ok(new AuthResponse(pair.AccessToken, pair.RefreshToken,
                pair.AccessTokenExpiresAt, await BuildUserDto(user, users, db, ct)));
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithName("Login")
        .WithSummary("Sign in with email and password.");

        // ------------------------------------------------------------------ google
        group.MapPost("/google", async (
            [FromBody] GoogleLoginRequest request,
            IConfiguration config,
            UserManager<AppUser> users,
            JwtTokenService tokens,
            AppDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var clientId = config["Authentication:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Problem(title: "Google sign-in not configured",
                    detail: "No Google client id is configured on the server.",
                    statusCode: StatusCodes.Status501NotImplemented);

            GoogleJsonWebSignature.Payload payload;
            try
            {
                // Verifies signature, issuer and expiry, and that the token was minted for this app.
                payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            }
            catch (InvalidJwtException ex)
            {
                loggerFactory.CreateLogger("Auth").LogWarning(ex, "Rejected Google credential.");
                return Results.Problem(title: "Invalid Google credential",
                    detail: "The Google token could not be verified.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!payload.EmailVerified)
                return ApiHelpers.Forbidden("This Google account does not have a verified email address.");

            var user = await users.FindByEmailAsync(payload.Email);

            // A Google account we have never seen registers itself, exactly like the signup
            // form, and waits for an administrator. It is given no role and no token.
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = payload.Email,
                    Email = payload.Email,
                    EmailConfirmed = true,
                    FullName = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
                    GoogleSubjectId = payload.Subject,
                    Status = UserStatus.PendingApproval,
                    RegistrationNote = "Signed up with Google.",
                };

                // No password: this account can only ever sign in through Google.
                var created = await users.CreateAsync(user);
                if (!created.Succeeded)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["registration"] = created.Errors.Select(e => e.Description).ToArray(),
                    });
                }

                await NotifyAdministratorsAsync(db, user, ct);
                return PendingOrBlocked(user);
            }

            if (user.IsDeleted) return ApiHelpers.Forbidden("This account is no longer active.");

            if (user.GoogleSubjectId is null)
                user.GoogleSubjectId = payload.Subject;
            else if (user.GoogleSubjectId != payload.Subject)
                return ApiHelpers.Forbidden("This email is already linked to a different Google account.");

            // An admin-created invitation is completed by the first federated sign-in.
            if (user.Status == UserStatus.Invited) user.Status = UserStatus.Active;
            user.EmailConfirmed = true;

            if (!CanSignIn(user))
            {
                // UpdateAsync refreshes the concurrency stamp; a bare SaveChanges would not.
                await users.UpdateAsync(user);
                return PendingOrBlocked(user);
            }

            user.LastLoginAt = DateTime.UtcNow;
            await users.UpdateAsync(user);

            var pair = await tokens.IssueAsync(user, ct);
            return Results.Ok(new AuthResponse(pair.AccessToken, pair.RefreshToken,
                pair.AccessTokenExpiresAt, await BuildUserDto(user, users, db, ct)));
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithName("GoogleLogin")
        .WithSummary("Sign in with a Google ID token. Unknown accounts register as pending.");

        // ------------------------------------------------------------------ tokens
        group.MapPost("/refresh", async (
            [FromBody] RefreshRequest request,
            JwtTokenService tokens,
            UserManager<AppUser> users,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var refreshed = await tokens.RefreshAsync(request.RefreshToken, ct);
            if (refreshed is null)
                return Results.Problem(title: "Invalid refresh token",
                    detail: "The refresh token is expired, revoked or unknown.",
                    statusCode: StatusCodes.Status401Unauthorized);

            var (pair, user) = refreshed.Value;
            return Results.Ok(new AuthResponse(pair.AccessToken, pair.RefreshToken,
                pair.AccessTokenExpiresAt, await BuildUserDto(user, users, db, ct)));
        })
        .AllowAnonymous()
        .WithName("RefreshToken");

        group.MapPost("/logout", async (
            [FromBody] RefreshRequest request,
            JwtTokenService tokens,
            CancellationToken ct) =>
        {
            await tokens.RevokeAsync(request.RefreshToken, ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("Logout");

        // ------------------------------------------------------------------ me
        group.MapGet("/me", async (
            ICurrentUser current,
            UserManager<AppUser> users,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (current.UserId is not { } id) return Results.Unauthorized();
            var user = await db.ActiveUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await BuildUserDto(user, users, db, ct));
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser");

        group.MapPut("/me/preferences", async (
            [FromBody] UpdatePreferencesRequest request,
            ICurrentUser current,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (current.UserId is not { } id) return Results.Unauthorized();

            if (!Enum.TryParse<ThemeMode>(request.ThemeMode, ignoreCase: true, out var mode))
                return ApiHelpers.BadRequest($"'{request.ThemeMode}' is not a valid theme mode.");

            // Scheme and font are opaque keys owned by the frontend catalogue. They are length
            // capped and stored verbatim; an unknown key simply falls back to the default there.
            var scheme = request.ColorScheme.Trim();
            var font = request.FontChoice.Trim();
            if (scheme.Length == 0 || font.Length == 0)
                return ApiHelpers.BadRequest("A colour scheme and a font must both be supplied.");

            var updated = await db.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.ThemeMode, mode)
                    .SetProperty(u => u.ColorScheme, scheme)
                    .SetProperty(u => u.FontChoice, font), ct);

            if (updated == 0) return ApiHelpers.NotFoundProblem("User not found.");

            return Results.Ok(new UserPreferencesDto(mode.ToString(), scheme, font));
        })
        .RequireAuthorization()
        .WithName("UpdatePreferences")
        .WithSummary("Save the signed-in user's colour scheme, font and light/dark preference.");

        group.MapPost("/change-password", async (
            [FromBody] ChangePasswordRequest request,
            ICurrentUser current,
            UserManager<AppUser> users,
            JwtTokenService tokens,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (current.UserId is not { } id) return Results.Unauthorized();

            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return Results.Unauthorized();

            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = result.Errors.Select(e => e.Description).ToArray()
                });

            // Force other sessions to re-authenticate with the new credential.
            await tokens.RevokeAllForUserAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("ChangePassword");
    }

    /// <summary>
    /// Explains, without issuing a token, why a correct credential still cannot sign in.
    /// The frontend keys off Status to show the right holding screen.
    /// </summary>
    private static IResult PendingOrBlocked(AppUser user) => user.Status switch
    {
        UserStatus.PendingApproval => Results.Json(
            new RegistrationResultDto("PendingApproval",
                "Your account is waiting for administrator approval.", user.Email ?? string.Empty),
            statusCode: StatusCodes.Status403Forbidden),

        UserStatus.Rejected => Results.Json(
            new RegistrationResultDto("Rejected",
                user.ApprovalNote is { Length: > 0 } note
                    ? $"Your access request was declined. {note}"
                    : "Your access request was declined. Contact an administrator.",
                user.Email ?? string.Empty),
            statusCode: StatusCodes.Status403Forbidden),

        UserStatus.Suspended => Results.Json(
            new RegistrationResultDto("Suspended",
                "This account is suspended. Contact an administrator.", user.Email ?? string.Empty),
            statusCode: StatusCodes.Status403Forbidden),

        _ => Results.Json(
            new RegistrationResultDto("Inactive",
                "This account is not active. Contact an administrator.", user.Email ?? string.Empty),
            statusCode: StatusCodes.Status403Forbidden),
    };

    /// <summary>Puts a new access request in front of every Super Admin.</summary>
    private static async Task NotifyAdministratorsAsync(AppDbContext db, AppUser applicant, CancellationToken ct)
    {
        var adminRoleId = await db.Roles
            .Where(r => r.Name == RoleNames.SuperAdmin)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (adminRoleId == Guid.Empty) return;

        var adminIds = await db.UserRoles
            .Where(ur => ur.RoleId == adminRoleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        foreach (var adminId in adminIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = adminId,
                Title = "A new account is awaiting approval",
                Body = $"{applicant.FullName} ({applicant.Email}) has requested access.",
                Link = "/settings/approvals",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    internal static async Task<CurrentUserDto> BuildUserDto(
        AppUser user, UserManager<AppUser> users, AppDbContext db, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var dept = user.DepartmentId is { } id
            ? await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct)
            : null;

        return new CurrentUserDto(
            user.Id, user.FullName, user.Email ?? string.Empty, user.JobTitle,
            user.DepartmentId, dept?.Name, dept?.Code, roles.ToArray(), user.Status.ToString(),
            new UserPreferencesDto(user.ThemeMode.ToString(), user.ColorScheme, user.FontChoice));
    }
}
