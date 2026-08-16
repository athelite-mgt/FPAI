using FpaiConnect.Application.Abstractions;
using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace FpaiConnect.Infrastructure.Persistence;

/// <summary>
/// Stamps audit columns and writes an append-only AuditLog row for every insert/update/delete.
/// Deletes on BaseEntity types are converted to soft deletes so nothing is lost.
/// </summary>
public class AuditInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Sensitive =
        ["PasswordHash", "SecurityStamp", "ConcurrencyStamp", "TokenHash", "ReplacedByTokenHash"];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = DateTime.UtcNow;
        var userId = currentUser.UserId;
        var userName = currentUser.UserName;
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog) continue;

            var isBase = entry.Entity is BaseEntity;
            string action;

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is BaseEntity addedBase)
                    {
                        addedBase.CreatedAt = now;
                        addedBase.CreatedById ??= userId;
                    }
                    action = "Created";
                    break;

                case EntityState.Modified:
                    if (entry.Entity is BaseEntity modBase)
                    {
                        modBase.UpdatedAt = now;
                        modBase.UpdatedById = userId;
                    }
                    action = "Updated";
                    break;

                case EntityState.Deleted when isBase:
                    // Soft delete: flip the flag instead of issuing a DELETE.
                    entry.State = EntityState.Modified;
                    ((BaseEntity)entry.Entity).IsDeleted = true;
                    ((BaseEntity)entry.Entity).UpdatedAt = now;
                    ((BaseEntity)entry.Entity).UpdatedById = userId;
                    action = "Deleted";
                    break;

                case EntityState.Deleted:
                    action = "Deleted";
                    break;

                default:
                    continue;
            }

            logs.Add(new AuditLog
            {
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = PrimaryKeyOf(entry),
                Action = action,
                UserId = userId,
                UserName = userName,
                Timestamp = now,
                Changes = Serialize(entry, action)
            });
        }

        if (logs.Count > 0) context.Set<AuditLog>().AddRange(logs);
    }

    private static string PrimaryKeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;
        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "");
        return string.Join(",", values);
    }

    private static string? Serialize(EntityEntry entry, string action)
    {
        if (action == "Created") return null;

        var changes = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (!prop.IsModified || prop.Metadata.Name is null) continue;
            if (Sensitive.Contains(prop.Metadata.Name)) continue;

            var oldValue = prop.OriginalValue;
            var newValue = prop.CurrentValue;
            if (Equals(oldValue, newValue)) continue;

            changes[prop.Metadata.Name] = new { from = oldValue?.ToString(), to = newValue?.ToString() };
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes);
    }
}
