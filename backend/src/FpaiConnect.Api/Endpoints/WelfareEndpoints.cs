using FpaiConnect.Api.Common;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Common;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using FpaiConnect.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FpaiConnect.Api.Endpoints;

public static class WelfareEndpoints
{
    public static void MapWelfareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/welfare/cases").WithTags("Welfare").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] WelfareStatus? status,
            [FromQuery] WelfareCategory? category,
            [FromQuery] CasePriority? priority,
            [FromQuery] Guid? playerId,
            [FromQuery] Guid? officerId,
            [FromQuery] bool? isDispute,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.WelfareCases.AsNoTracking()
                .Include(c => c.Player)
                .Include(c => c.AssignedOfficer)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(c => c.CaseNumber.Contains(term)
                                         || c.Title.Contains(term)
                                         || c.Player.FullName.Contains(term));
            }
            if (status is { } s) query = query.Where(c => c.Status == s);
            if (category is { } cat) query = query.Where(c => c.Category == cat);
            if (priority is { } p) query = query.Where(c => c.Priority == p);
            if (playerId is { } pid) query = query.Where(c => c.PlayerId == pid);
            if (officerId is { } oid) query = query.Where(c => c.AssignedOfficerId == oid);
            if (isDispute is { } d) query = query.Where(c => c.IsDispute == d);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "casenumber" => page.SortDescending
                    ? query.OrderByDescending(c => c.CaseNumber) : query.OrderBy(c => c.CaseNumber),
                "priority" => page.SortDescending
                    ? query.OrderByDescending(c => c.Priority) : query.OrderBy(c => c.Priority),
                "status" => page.SortDescending
                    ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status),
                _ => page.SortDescending
                    ? query.OrderByDescending(c => c.OpenedAt) : query.OrderBy(c => c.OpenedAt)
            };

            var result = await query
                .Select(c => new WelfareCaseListDto(c.Id, c.CaseNumber, c.Title, c.PlayerId,
                    c.Player.FullName, c.Category, c.Priority, c.Status, c.AssignedOfficerId,
                    c.AssignedOfficer != null ? c.AssignedOfficer.FullName : null,
                    c.IsDispute, c.OpenedAt, c.ResolvedAt))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.WelfareRead)
        .WithName("ListWelfareCases");

        group.MapGet("/stats", async (AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var scoped = db.WelfareCases.AsNoTracking().WhereReadable(current);

            var byStatus = await scoped
                .GroupBy(c => c.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var byCategory = await scoped
                .GroupBy(c => c.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                OpenCases = byStatus.Where(x => x.Status < WelfareStatus.Resolved).Sum(x => x.Count),
                ClosedCases = byStatus.Where(x => x.Status >= WelfareStatus.Resolved).Sum(x => x.Count),
                Disputes = await scoped.CountAsync(c => c.IsDispute, ct),
                WelfareRequests = await scoped.CountAsync(c => !c.IsDispute && c.Status < WelfareStatus.Resolved, ct),
                ByStatus = byStatus.Select(x => new CountByLabel(x.Status.ToString(), x.Count)).ToList(),
                ByCategory = byCategory.Select(x => new CountByLabel(x.Category.ToString(), x.Count)).ToList()
            });
        })
        .RequireAuthorization(Policies.WelfareRead)
        .WithName("WelfareStats");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var entity = await LoadDetail(db, id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Welfare case not found.");
            if (!current.CanReadDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this department's cases.");

            return Results.Ok(ToDetail(entity));
        })
        .RequireAuthorization(Policies.WelfareRead)
        .WithName("GetWelfareCase");

        group.MapPost("/", async (
            [FromBody] WelfareCaseUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var dept = await db.Departments.FirstOrDefaultAsync(d => d.Code == DepartmentCodes.Welfare, ct);
            if (dept is null) return ApiHelpers.BadRequest("The welfare department is not configured.");
            if (!current.CanWriteDepartment(dept.Id))
                return ApiHelpers.Forbidden("You cannot create cases in the welfare department.");

            if (!await db.Players.AnyAsync(p => p.Id == request.PlayerId, ct))
                return ApiHelpers.BadRequest("The selected member does not exist.");
            if (request.AssignedOfficerId is { } officer && !await db.ActiveUsers.AnyAsync(u => u.Id == officer, ct))
                return ApiHelpers.BadRequest("The selected officer does not exist.");

            var entity = new WelfareCase
            {
                CaseNumber = await refs.NextWelfareCaseAsync(ct),
                DepartmentId = dept.Id,
                PlayerId = request.PlayerId,
                Category = request.Category,
                Priority = request.Priority,
                Status = request.AssignedOfficerId is null ? WelfareStatus.New : WelfareStatus.Assigned,
                AssignedOfficerId = request.AssignedOfficerId,
                Title = request.Title.Trim(),
                Description = request.Description,
                IsDispute = request.IsDispute,
                OpenedAt = DateTime.UtcNow
            };
            db.WelfareCases.Add(entity);
            db.WelfareCaseNotes.Add(new WelfareCaseNote
            {
                WelfareCaseId = entity.Id,
                Note = "Case registered.",
                StatusAtNote = entity.Status,
                AuthorId = current.UserId
            });
            await db.SaveChangesAsync(ct);

            var created = await LoadDetail(db, entity.Id, ct);
            return Results.Created($"/api/welfare/cases/{entity.Id}", ToDetail(created!));
        })
        .RequireAuthorization(Policies.WelfareWrite)
        .WithName("CreateWelfareCase");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] WelfareCaseUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var entity = await db.WelfareCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Welfare case not found.");
            if (!current.CanWriteDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this department's cases.");
            if (entity.Status == WelfareStatus.Closed)
                return ApiHelpers.Conflict("A closed case cannot be edited. Reopen it first.");

            if (!await db.Players.AnyAsync(p => p.Id == request.PlayerId, ct))
                return ApiHelpers.BadRequest("The selected member does not exist.");

            entity.Title = request.Title.Trim();
            entity.PlayerId = request.PlayerId;
            entity.Category = request.Category;
            entity.Priority = request.Priority;
            entity.AssignedOfficerId = request.AssignedOfficerId;
            entity.IsDispute = request.IsDispute;
            entity.Description = request.Description;
            entity.Resolution = request.Resolution;
            await db.SaveChangesAsync(ct);

            var updated = await LoadDetail(db, id, ct);
            return Results.Ok(ToDetail(updated!));
        })
        .RequireAuthorization(Policies.WelfareWrite)
        .WithName("UpdateWelfareCase");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<WelfareStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid welfare status.");

            var entity = await db.WelfareCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Welfare case not found.");
            if (!current.CanWriteDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this department's cases.");

            // Closing a case is an approval-grade action reserved for heads and admins.
            if (target == WelfareStatus.Closed && !current.CanApproveDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can close a case.");

            if (!WorkflowRules.CanTransition(WorkflowRules.Welfare, entity.Status, target))
                return ApiHelpers.Conflict(
                    $"A case cannot move from {entity.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.Welfare, entity.Status))}.");

            var previous = entity.Status;
            entity.Status = target;
            if (target == WelfareStatus.Resolved) entity.ResolvedAt = DateTime.UtcNow;
            if (target == WelfareStatus.Closed) entity.ClosedAt = DateTime.UtcNow;
            if (target == WelfareStatus.InProgress && previous == WelfareStatus.Closed)
            {
                entity.ClosedAt = null;
                entity.ResolvedAt = null;
            }

            db.WelfareCaseNotes.Add(new WelfareCaseNote
            {
                WelfareCaseId = entity.Id,
                Note = string.IsNullOrWhiteSpace(request.Comment)
                    ? $"Status changed from {previous} to {target}."
                    : $"Status changed from {previous} to {target}. {request.Comment}",
                StatusAtNote = target,
                AuthorId = current.UserId
            });
            await db.SaveChangesAsync(ct);

            var updated = await LoadDetail(db, id, ct);
            return Results.Ok(ToDetail(updated!));
        })
        .RequireAuthorization(Policies.WelfareWrite)
        .WithName("TransitionWelfareCase");

        group.MapPost("/{id:guid}/notes", async (
            Guid id, [FromBody] AddNoteRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var entity = await db.WelfareCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Welfare case not found.");
            if (!current.CanWriteDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You cannot add notes to this department's cases.");

            var note = new WelfareCaseNote
            {
                WelfareCaseId = id,
                Note = request.Note.Trim(),
                StatusAtNote = null,
                AuthorId = current.UserId
            };
            db.WelfareCaseNotes.Add(note);
            await db.SaveChangesAsync(ct);

            var authorName = current.UserName;
            return Results.Created($"/api/welfare/cases/{id}",
                new CaseNoteDto(note.Id, note.Note, null, authorName, note.CreatedAt));
        })
        .RequireAuthorization(Policies.WelfareWrite)
        .WithName("AddWelfareNote");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var entity = await db.WelfareCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Welfare case not found.");
            if (!current.CanApproveDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can delete a case.");

            db.WelfareCases.Remove(entity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.WelfareApprove)
        .WithName("DeleteWelfareCase");
    }

    private static Task<WelfareCase?> LoadDetail(AppDbContext db, Guid id, CancellationToken ct) =>
        db.WelfareCases.AsNoTracking()
            .Include(c => c.Player).ThenInclude(p => p.CurrentClub)
            .Include(c => c.Department)
            .Include(c => c.AssignedOfficer)
            .Include(c => c.Notes.OrderByDescending(n => n.CreatedAt)).ThenInclude(n => n.Author)
            .Include(c => c.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private static WelfareCaseDetailDto ToDetail(WelfareCase c) => new(
        c.Id, c.CaseNumber, c.Title, c.Description, c.Resolution,
        c.PlayerId, c.Player.FullName, c.Player.CurrentClub?.Name,
        c.DepartmentId, c.Department.Name,
        c.Category, c.Priority, c.Status,
        c.AssignedOfficerId, c.AssignedOfficer?.FullName, c.IsDispute,
        c.OpenedAt, c.ResolvedAt, c.ClosedAt,
        c.Notes.Select(n => new CaseNoteDto(n.Id, n.Note, n.StatusAtNote?.ToString(),
            n.Author?.FullName, n.CreatedAt)).ToList(),
        c.Documents.Select(DocumentEndpoints.ToListDto).ToList(),
        WorkflowRules.Next(WorkflowRules.Welfare, c.Status).Select(s => s.ToString()).ToList());
}
