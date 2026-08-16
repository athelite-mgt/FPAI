using FpaiConnect.Api.Common;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Common;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FpaiConnect.Api.Endpoints;

public static class DocumentEndpoints
{
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    /// <summary>Extensions we refuse outright; everything else is stored but never executed.</summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".scr", ".ps1", ".sh", ".jar", ".vbs", ".js"
    };

    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] DocumentCategory? category,
            [FromQuery] Guid? departmentId,
            [FromQuery] Guid? welfareCaseId,
            [FromQuery] Guid? legalCaseId,
            [FromQuery] Guid? voucherId,
            [FromQuery] Guid? expenseId,
            [FromQuery] Guid? meetingId,
            [FromQuery] Guid? eventId,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.Documents.AsNoTracking()
                .Include(d => d.Department)
                .Include(d => d.UploadedBy)
                .WhereReadable(current);

            // Confidential documents are visible only to the owning department, heads and admins.
            if (!current.IsSuperAdmin && !current.IsInRole(RoleNames.DepartmentHead))
                query = query.Where(d => !d.IsConfidential || d.DepartmentId == current.DepartmentId);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(d => d.Title.Contains(term) || d.FileName.Contains(term));
            }
            if (category is { } c) query = query.Where(d => d.Category == c);
            if (departmentId is { } dept) query = query.Where(d => d.DepartmentId == dept);
            if (welfareCaseId is { } w) query = query.Where(d => d.WelfareCaseId == w);
            if (legalCaseId is { } l) query = query.Where(d => d.LegalCaseId == l);
            if (voucherId is { } v) query = query.Where(d => d.VoucherId == v);
            if (expenseId is { } e) query = query.Where(d => d.ExpenseId == e);
            if (meetingId is { } m) query = query.Where(d => d.MeetingId == m);
            if (eventId is { } ev) query = query.Where(d => d.EventId == ev);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "title" => page.SortDescending ? query.OrderByDescending(d => d.Title) : query.OrderBy(d => d.Title),
                "size" => page.SortDescending ? query.OrderByDescending(d => d.SizeBytes) : query.OrderBy(d => d.SizeBytes),
                _ => page.SortDescending ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt)
            };

            var result = await query.ToPagedResultAsync(page, ToListDto, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.DocumentsRead)
        .WithName("ListDocuments");

        group.MapPost("/", async (
            HttpRequest http, AppDbContext db, ICurrentUser current,
            IFileStorage storage, CancellationToken ct) =>
        {
            if (!http.HasFormContentType) return ApiHelpers.BadRequest("Expected a multipart form upload.");

            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return ApiHelpers.BadRequest("No file was supplied.");
            if (file.Length > MaxUploadBytes)
                return ApiHelpers.BadRequest($"Files must be {MaxUploadBytes / (1024 * 1024)} MB or smaller.");

            var extension = Path.GetExtension(file.FileName);
            if (BlockedExtensions.Contains(extension))
                return ApiHelpers.BadRequest($"Files of type {extension} are not accepted.");

            Guid departmentId;
            if (Guid.TryParse(form["departmentId"], out var parsedDept)) departmentId = parsedDept;
            else if (current.DepartmentId is { } own) departmentId = own;
            else return ApiHelpers.BadRequest("A department must be supplied.");

            if (!current.CanWriteDepartment(departmentId))
                return ApiHelpers.Forbidden("You cannot upload documents for this department.");
            if (!await db.Departments.AnyAsync(d => d.Id == departmentId, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");

            var category = Enum.TryParse<DocumentCategory>(form["category"], true, out var cat)
                ? cat : DocumentCategory.Other;

            await using var stream = file.OpenReadStream();
            var stored = await storage.SaveAsync(stream, file.FileName, file.ContentType, ct);

            var document = new Document
            {
                Title = string.IsNullOrWhiteSpace(form["title"]) ? file.FileName : form["title"].ToString().Trim(),
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = stored.SizeBytes,
                StoragePath = stored.StoragePath,
                Sha256 = stored.Sha256,
                Category = category,
                IsConfidential = form["isConfidential"] == "true",
                Description = form["description"],
                DepartmentId = departmentId,
                UploadedById = current.UserId,
                WelfareCaseId = ParseNullableGuid(form["welfareCaseId"]),
                LegalCaseId = ParseNullableGuid(form["legalCaseId"]),
                VoucherId = ParseNullableGuid(form["voucherId"]),
                ExpenseId = ParseNullableGuid(form["expenseId"]),
                MeetingId = ParseNullableGuid(form["meetingId"]),
                EventId = ParseNullableGuid(form["eventId"])
            };

            db.Documents.Add(document);
            await db.SaveChangesAsync(ct);

            var saved = await db.Documents.AsNoTracking()
                .Include(d => d.Department).Include(d => d.UploadedBy)
                .FirstAsync(d => d.Id == document.Id, ct);

            return Results.Created($"/api/documents/{document.Id}", ToListDto(saved));
        })
        .RequireAuthorization(Policies.DocumentsWrite)
        .DisableAntiforgery()
        .WithName("UploadDocument");

        group.MapGet("/{id:guid}/download", async (
            Guid id, AppDbContext db, ICurrentUser current, IFileStorage storage, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return ApiHelpers.NotFoundProblem("Document not found.");
            if (!current.CanReadDepartment(document.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this document.");
            if (document.IsConfidential && !current.IsSuperAdmin
                && !current.IsInRole(RoleNames.DepartmentHead)
                && document.DepartmentId != current.DepartmentId)
                return ApiHelpers.Forbidden("This document is marked confidential.");

            var stream = await storage.OpenReadAsync(document.StoragePath, ct);
            if (stream is null) return ApiHelpers.NotFoundProblem("The stored file is missing.");

            return Results.File(stream, document.ContentType, document.FileName, enableRangeProcessing: true);
        })
        .RequireAuthorization(Policies.DocumentsRead)
        .WithName("DownloadDocument");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] DocumentUpdateRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return ApiHelpers.NotFoundProblem("Document not found.");
            if (!current.CanWriteDepartment(document.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this document.");

            document.Title = request.Title.Trim();
            document.Category = request.Category;
            document.IsConfidential = request.IsConfidential;
            document.Description = request.Description;
            await db.SaveChangesAsync(ct);

            var saved = await db.Documents.AsNoTracking()
                .Include(d => d.Department).Include(d => d.UploadedBy)
                .FirstAsync(d => d.Id == id, ct);
            return Results.Ok(ToListDto(saved));
        })
        .RequireAuthorization(Policies.DocumentsWrite)
        .WithName("UpdateDocument");

        group.MapDelete("/{id:guid}", async (
            Guid id, AppDbContext db, ICurrentUser current, IFileStorage storage, CancellationToken ct) =>
        {
            var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (document is null) return ApiHelpers.NotFoundProblem("Document not found.");
            if (!current.CanWriteDepartment(document.DepartmentId))
                return ApiHelpers.Forbidden("You cannot delete this document.");

            // The row is soft-deleted for audit; the bytes go immediately.
            db.Documents.Remove(document);
            await db.SaveChangesAsync(ct);
            await storage.DeleteAsync(document.StoragePath, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.DocumentsWrite)
        .WithName("DeleteDocument");
    }

    private static Guid? ParseNullableGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;

    internal static DocumentListDto ToListDto(Document d)
    {
        var (linkedTo, linkedId) = d switch
        {
            { WelfareCaseId: { } w } => ("WelfareCase", (Guid?)w),
            { LegalCaseId: { } l } => ("LegalCase", l),
            { VoucherId: { } v } => ("Voucher", v),
            { ExpenseId: { } e } => ("Expense", e),
            { MeetingId: { } m } => ("Meeting", m),
            { EventId: { } ev } => ("Event", ev),
            _ => (null, (Guid?)null)
        };

        return new DocumentListDto(d.Id, d.Title, d.FileName, d.ContentType, d.SizeBytes,
            d.Category, d.IsConfidential, d.Version, d.DepartmentId, d.Department?.Name,
            d.UploadedBy?.FullName, d.CreatedAt, linkedTo, linkedId);
    }
}
