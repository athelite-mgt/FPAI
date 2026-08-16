namespace FpaiConnect.Api.Security;

public static class SecurityHeaders
{
    /// <summary>
    /// Baseline hardening headers. The CSP is deliberately strict but allows Google Identity
    /// Services, which the sign-in page loads for the Google button, and the Microsoft
    /// identity platform, which MSAL.js (bundled, not loaded from a CDN) calls directly for
    /// the redirect-based sign-in flow.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

            if (!context.Request.Path.StartsWithSegments("/swagger"))
            {
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' https://accounts.google.com https://apis.google.com; " +
                    "style-src 'self' 'unsafe-inline' https://accounts.google.com; " +
                    "img-src 'self' data: https:; " +
                    "font-src 'self' data:; " +
                    "connect-src 'self' https://accounts.google.com https://login.microsoftonline.com; " +
                    "frame-src https://accounts.google.com; " +
                    "object-src 'none'; base-uri 'self'; form-action 'self'";
            }

            await next();
        });
}
