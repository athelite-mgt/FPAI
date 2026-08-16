using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FpaiConnect.Api.Common;

/// <summary>
/// Declares the JWT bearer scheme on the generated OpenAPI document so the Swagger UI
/// offers an Authorize box for the token returned by /api/auth/login.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "FPAI Connect API",
            Version = "v1",
            Description = "Player welfare, legal, finance, governance and operations "
                          + "for the Football Players Association of India."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token returned by /api/auth/login."
        };

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            }
        ];

        return Task.CompletedTask;
    }
}
