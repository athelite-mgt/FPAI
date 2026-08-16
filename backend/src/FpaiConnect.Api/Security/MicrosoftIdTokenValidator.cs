using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;

namespace FpaiConnect.Api.Security;

public record MicrosoftIdentity(string Subject, string TenantId, string? Email, string? Name);

/// <summary>
/// Verifies a client-submitted Microsoft ID token and extracts the identity it vouches for.
/// An interface so tests can substitute a fake without needing a real Microsoft-signed token,
/// which — unlike our own JWTs — cannot be forged in a test without a live tenant.
/// </summary>
public interface IMicrosoftIdTokenValidator
{
    bool IsConfigured { get; }

    /// <summary>Returns null when the token is missing, malformed, expired or fails signature checks.</summary>
    Task<MicrosoftIdentity?> ValidateAsync(string idToken, CancellationToken ct);
}

/// <summary>
/// Validates an ID token issued by the Microsoft identity platform for a public client
/// (MSAL.js), without going through ASP.NET's JwtBearer middleware — that middleware
/// authenticates *this API's own* bearer tokens, whereas this is a one-off client-submitted
/// credential exchanged for one of ours, exactly like GoogleJsonWebSignature.ValidateAsync
/// is for the Google flow.
///
/// The app is registered to accept "any Microsoft account" (work, school or personal), so
/// tokens are issued against the multi-tenant `common` authority and the issuer is
/// per-tenant (https://login.microsoftonline.com/{tenantId}/v2.0) rather than one fixed
/// string — the issuer validator below accepts any well-formed tenant GUID rather than a
/// single expected issuer.
/// </summary>
public class MicrosoftIdTokenValidator : IMicrosoftIdTokenValidator
{
    private const string Authority = "https://login.microsoftonline.com/common/v2.0";

    private static readonly Regex IssuerPattern = new(
        @"^https://login\.microsoftonline\.com/[0-9a-fA-F-]{36}/v2\.0$",
        RegexOptions.Compiled);

    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager = new(
        $"{Authority}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever());

    private readonly string _clientId;

    public MicrosoftIdTokenValidator(IConfiguration config)
    {
        _clientId = config["Authentication:Microsoft:ClientId"] ?? string.Empty;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId);

    public async Task<MicrosoftIdentity?> ValidateAsync(string idToken, CancellationToken ct)
    {
        if (!IsConfigured) return null;

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await _configurationManager.GetConfigurationAsync(ct);
        }
        catch (Exception)
        {
            // The discovery document could not be fetched; fail closed rather than skip validation.
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            IssuerValidator = (issuer, _, _) =>
                issuer is not null && IssuerPattern.IsMatch(issuer)
                    ? issuer
                    : throw new SecurityTokenInvalidIssuerException("Unrecognised Microsoft token issuer."),
            ValidateAudience = true,
            ValidAudience = _clientId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
        };

        var handler = new JwtSecurityTokenHandler();
        ClaimsPrincipalResult result;
        try
        {
            var principal = handler.ValidateToken(idToken, parameters, out var validatedToken);
            result = new ClaimsPrincipalResult(principal, (JwtSecurityToken)validatedToken);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Malformed token string.
            return null;
        }

        var token = result.Token;
        var subject = token.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject)) return null;

        var tenantId = token.Claims.FirstOrDefault(c => c.Type == "tid")?.Value ?? string.Empty;
        var email = token.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == "preferred_username")?.Value;
        var name = token.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

        // A personal Microsoft account (MSA) has no @domain guarantee on preferred_username in
        // the way a work/school account does, but it is still a usable, stable email address.
        return new MicrosoftIdentity(subject, tenantId, email, name);
    }

    private sealed record ClaimsPrincipalResult(System.Security.Claims.ClaimsPrincipal Principal, JwtSecurityToken Token);
}
