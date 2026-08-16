using FpaiConnect.Api.Common;
using FpaiConnect.Api.Endpoints;
using FpaiConnect.Api.Security;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Infrastructure;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---------- Configuration ----------
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(
            "Jwt:SigningKey must be set to at least 32 characters in production. " +
            "Provide it via App Service configuration or Key Vault.");

    // Development convenience only; never reached in production.
    jwtOptions.SigningKey = "fpai-connect-development-signing-key-do-not-use-in-production";
    builder.Configuration["Jwt:SigningKey"] = jwtOptions.SigningKey;
    builder.Services.Configure<JwtOptions>(o => o.SigningKey = jwtOptions.SigningKey);
}

// ---------- Services ----------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAppAuthorization();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    // Enums travel as their names so the API contract stays readable and stable.
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
// .NET 10 generates the OpenAPI document natively; Swashbuckle only renders the UI.
builder.Services.AddOpenApi("v1", options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

const string CorsPolicy = "FpaiSpa";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? ["http://localhost:5173"];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

// Brute-force and signup-spam protection.
//
// These MUST be partitioned by client, not global: a single fixed window shared by every
// caller would let one busy office IP lock the whole association out of signing in.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var signInPerMinute = builder.Configuration.GetValue("RateLimiting:SignInPerMinutePerClient", 20);
    var registerPerHour = builder.Configuration.GetValue("RateLimiting:RegisterPerHourPerClient", 5);

    static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = signInPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Registration is rare and anonymous, so it is held to a much tighter budget.
    o.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = registerPerHour,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// ---------- Pipeline ----------
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
    logger.LogError(feature?.Error, "Unhandled exception on {Path}", context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new
    {
        type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        title = "An unexpected error occurred.",
        status = 500,
        traceId = context.TraceIdentifier
    });
}));

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "FPAI Connect API v1"));
}
else
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Serve the built SPA when it has been published alongside the API (single App Service deployment).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
    .AllowAnonymous()
    .WithTags("System");

app.MapAuthEndpoints();
app.MapDirectoryEndpoints();
app.MapWelfareEndpoints();
app.MapLegalEndpoints();
app.MapFinanceEndpoints();
app.MapGovernanceEndpoints();
app.MapOperationsEndpoints();
app.MapDocumentEndpoints();
app.MapWorkEndpoints();
app.MapReportEndpoints();
app.MapUserEndpoints();
app.MapDashboardEndpoints();

// SPA fallback: any non-API route is handled by React Router.
app.MapFallbackToFile("index.html");

// ---------- Startup migration + seed ----------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (app.Configuration.GetValue("Seed:Enabled", true))
        await services.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

await app.RunAsync();

/// <summary>Exposed so the integration test host can reference this assembly.</summary>
public partial class Program;
