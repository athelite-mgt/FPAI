using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

/// <summary>
/// Stored file metadata. The bytes live behind IFileStorage (local disk now, Azure Blob in Azure),
/// so StoragePath is an opaque provider key rather than a filesystem path.
/// </summary>
public class Document : BaseEntity, IDepartmentScoped
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? Sha256 { get; set; }

    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public bool IsConfidential { get; set; }
    public int Version { get; set; } = 1;
    public string? Description { get; set; }

    public Guid? UploadedById { get; set; }
    public AppUser? UploadedBy { get; set; }

    // Optional links to the record this document supports.
    public Guid? WelfareCaseId { get; set; }
    public WelfareCase? WelfareCase { get; set; }
    public Guid? LegalCaseId { get; set; }
    public LegalCase? LegalCase { get; set; }
    public Guid? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public Guid? ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public Guid? MeetingId { get; set; }
    public Meeting? Meeting { get; set; }
    public Guid? EventId { get; set; }
    public Event? Event { get; set; }
}
