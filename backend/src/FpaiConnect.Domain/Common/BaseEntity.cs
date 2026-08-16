namespace FpaiConnect.Domain.Common;

/// <summary>Base for all persisted aggregate roots. Guid keys keep Azure SQL inserts contention-free.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    /// <summary>Soft delete. Every query filters this out via a global query filter.</summary>
    public bool IsDeleted { get; set; }
}

/// <summary>Marks an entity as owned by a department, which drives row-level authorization.</summary>
public interface IDepartmentScoped
{
    Guid DepartmentId { get; set; }
}
