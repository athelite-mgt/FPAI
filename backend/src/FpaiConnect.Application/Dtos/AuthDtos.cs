using System.ComponentModel.DataAnnotations;

namespace FpaiConnect.Application.Dtos;

public record LoginRequest
{
    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string Password { get; init; } = string.Empty;
}

public record GoogleLoginRequest
{
    /// <summary>The ID token returned by Google Identity Services in the browser.</summary>
    [Required]
    public string Credential { get; init; } = string.Empty;
}

public record RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public record ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; init; } = string.Empty;
    [Required, MinLength(10), MaxLength(200)] public string NewPassword { get; init; } = string.Empty;
}

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    CurrentUserDto User);

public record CurrentUserDto(
    Guid Id,
    string FullName,
    string Email,
    string? JobTitle,
    Guid? DepartmentId,
    string? DepartmentName,
    string? DepartmentCode,
    IReadOnlyList<string> Roles,
    string Status,
    UserPreferencesDto Preferences);

/// <summary>Interface preferences, stored per user so they follow the person between devices.</summary>
public record UserPreferencesDto(string ThemeMode, string ColorScheme, string FontChoice);

public record UpdatePreferencesRequest
{
    /// <summary>System, Light or Dark.</summary>
    [Required, MaxLength(20)] public string ThemeMode { get; init; } = "System";

    [Required, MaxLength(40)] public string ColorScheme { get; init; } = "pitch";

    [Required, MaxLength(40)] public string FontChoice { get; init; } = "sans";
}

// ---------------------------------------------------------------- self-registration
public record RegisterRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(200)] public string Email { get; init; } = string.Empty;

    [Required, MinLength(10), MaxLength(200)] public string Password { get; init; } = string.Empty;

    [MaxLength(150)] public string? JobTitle { get; init; }

    /// <summary>Optional context for the administrator reviewing the request.</summary>
    [MaxLength(1000)] public string? Note { get; init; }
}

/// <summary>
/// Returned by registration and by a sign-in attempt on an account that is not yet approved.
/// No token is issued in either case.
/// </summary>
public record RegistrationResultDto(string Status, string Message, string Email);

public record PendingUserDto(
    Guid Id,
    string FullName,
    string Email,
    string? JobTitle,
    string? RegistrationNote,
    bool SignedUpWithGoogle,
    DateTime CreatedAt,
    string Status);

public record ApproveUserRequest
{
    [Required] public string Role { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    [MaxLength(1000)] public string? Note { get; init; }
}

public record RejectUserRequest
{
    [Required, MaxLength(1000)] public string Reason { get; init; } = string.Empty;
}
