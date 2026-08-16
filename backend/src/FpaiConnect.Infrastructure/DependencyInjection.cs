using FpaiConnect.Application.Abstractions;
using FpaiConnect.Domain.Entities;
using FpaiConnect.Infrastructure.Persistence;
using FpaiConnect.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FpaiConnect.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<AuditInterceptor>();

        // Provider is chosen by configuration: Sqlite for local development, SqlServer for Azure SQL.
        // Switching environments is a connection-string + provider-name change, nothing more.
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connectionString = config.GetConnectionString("Default")
                               ?? "Data Source=fpai-connect.db";

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());

            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                });

                // Entra-authenticated servers carry no password anywhere: the connection
                // string just names the identity mode, and Microsoft.Data.SqlClient asks
                // Azure.Identity for a token itself using the App Service's managed identity.
                // Locally (Azure CLI / Visual Studio login) the same mode falls back to that
                // developer credential, so no code path needs a SQL login at all.
            }
            else
            {
                options.UseSqlite(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            }
        });

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = true;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Documents live on disk in development and in Blob Storage on App Service, whose
        // local filesystem is ephemeral and not shared between instances.
        var storageProvider = config["Storage:Provider"] ?? "Local";
        if (storageProvider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        else
            services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<Services.ReferenceNumberGenerator>();

        return services;
    }
}
