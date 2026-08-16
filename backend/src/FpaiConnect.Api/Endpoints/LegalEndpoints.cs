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

public static class LegalEndpoints
{
    private static readonly Dictionary<LegalCaseType, string> CasePrefixes = new()
    {
        [LegalCaseType.FifaDrc] = "FIFA/DRC",
        [LegalCaseType.Cas] = "CAS",
        [LegalCaseType.Psc] = "FIFA/PSC",
        [LegalCaseType.Arbitration] = "ARB"
    };

    public static void MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/legal/cases").WithTags("Legal").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] LegalStatus? status,
            [FromQuery] LegalCaseType? type,
            [FromQuery] LegalOutcome? outcome,
            [FromQuery] Guid? playerId,
            [FromQuery] Guid? counselId,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.LegalCases.AsNoTracking()
                .Include(c => c.Player)
                .Include(c => c.OpposingClub)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(c => c.CaseNumber.Contains(term)
                                         || c.Title.Contains(term)
                                         || c.Player.FullName.Contains(term)
                                         || (c.LawyerName != null && c.LawyerName.Contains(term)));
            }
            if (status is { } s) query = query.Where(c => c.Status == s);
            if (type is { } t) query = query.Where(c => c.Type == t);
            if (outcome is { } o) query = query.Where(c => c.Outcome == o);
            if (playerId is { } pid) query = query.Where(c => c.PlayerId == pid);
            if (counselId is { } cid) query = query.Where(c => c.AssignedCounselId == cid);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "casenumber" => page.SortDescending
                    ? query.OrderByDescending(c => c.CaseNumber) : query.OrderBy(c => c.CaseNumber),
                "hearing" => page.SortDescending
                    ? query.OrderByDescending(c => c.HearingDate) : query.OrderBy(c => c.HearingDate),
                "claim" => page.SortDescending
                    ? query.OrderByDescending(c => c.ClaimAmount) : query.OrderBy(c => c.ClaimAmount),
                _ => page.SortDescending
                    ? query.OrderByDescending(c => c.FiledAt) : query.OrderBy(c => c.FiledAt)
            };

            var result = await query
                .Select(c => new LegalCaseListDto(c.Id, c.CaseNumber, c.Title, c.PlayerId,
                    c.Player.FullName, c.OpposingClub != null ? c.OpposingClub.Name : null,
                    c.Type, c.Status, c.Outcome, c.Priority, c.LawyerName, c.ClaimAmount, c.Currency,
                    c.FiledAt, c.HearingDate))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.LegalRead)
        .WithName("ListLegalCases");

        group.MapGet("/stats", async (AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var scoped = db.LegalCases.AsNoTracking().WhereReadable(current);

            var byType = await scoped.GroupBy(c => c.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(ct);
            var byStatus = await scoped.GroupBy(c => c.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);

            return Results.Ok(new
            {
                Active = await scoped.CountAsync(c => c.Status != LegalStatus.Closed, ct),
                Closed = await scoped.CountAsync(c => c.Status == LegalStatus.Closed, ct),
                UpcomingHearings = await scoped.CountAsync(
                    c => c.HearingDate != null && c.HearingDate > DateTime.UtcNow, ct),
                TotalClaimed = await scoped.SumAsync(c => c.ClaimAmount ?? 0m, ct),
                TotalAwarded = await scoped.SumAsync(c => c.AwardedAmount ?? 0m, ct),
                ByType = byType.Select(x => new CountByLabel(x.Type.ToString(), x.Count)).ToList(),
                ByStatus = byStatus.Select(x => new CountByLabel(x.Status.ToString(), x.Count)).ToList()
            });
        })
        .RequireAuthorization(Policies.LegalRead)
        .WithName("LegalStats");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var entity = await LoadDetail(db, id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Legal case not found.");
            if (!current.CanReadDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this department's cases.");
            return Results.Ok(ToDetail(entity));
        })
        .RequireAuthorization(Policies.LegalRead)
        .WithName("GetLegalCase");

        group.MapPost("/", async (
            [FromBody] LegalCaseUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var dept = await db.Departments.FirstOrDefaultAsync(d => d.Code == DepartmentCodes.Legal, ct);
            if (dept is null) return ApiHelpers.BadRequest("The legal department is not configured.");
            if (!current.CanWriteDepartment(dept.Id))
                return ApiHelpers.Forbidden("You cannot create cases in the legal department.");

            if (!await db.Players.AnyAsync(p => p.Id == request.PlayerId, ct))
                return ApiHelpers.BadRequest("The selected member does not exist.");
            if (request.OpposingClubId is { } club && !await db.Clubs.AnyAsync(c => c.Id == club, ct))
                return ApiHelpers.BadRequest("The selected club does not exist.");

            var entity = new LegalCase
            {
                CaseNumber = await refs.NextLegalCaseAsync(CasePrefixes[request.Type], ct),
                DepartmentId = dept.Id,
                PlayerId = request.PlayerId,
                OpposingClubId = request.OpposingClubId,
                Type = request.Type,
                Status = LegalStatus.Registered,
                Outcome = LegalOutcome.Pending,
                Priority = request.Priority,
                LawyerName = request.LawyerName,
                LawyerFirm = request.LawyerFirm,
                AssignedCounselId = request.AssignedCounselId,
                Title = request.Title.Trim(),
                Description = request.Description,
                ClaimAmount = request.ClaimAmount,
                HearingDate = request.HearingDate,
                FiledAt = DateTime.UtcNow
            };
            db.LegalCases.Add(entity);
            db.LegalCaseEvents.Add(new LegalCaseEvent
            {
                LegalCaseId = entity.Id,
                Title = "Case Registered",
                Detail = "Matter opened on the FPAI legal register.",
                OccurredAt = DateTime.UtcNow,
                StatusAtEvent = LegalStatus.Registered,
                AuthorId = current.UserId
            });
            await db.SaveChangesAsync(ct);

            var created = await LoadDetail(db, entity.Id, ct);
            return Results.Created($"/api/legal/cases/{entity.Id}", ToDetail(created!));
        })
        .RequireAuthorization(Policies.LegalWrite)
        .WithName("CreateLegalCase");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] LegalCaseUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var entity = await db.LegalCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Legal case not found.");
            if (!current.CanWriteDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this department's cases.");
            if (entity.Status == LegalStatus.Closed)
                return ApiHelpers.Conflict("A closed matter cannot be edited.");

            if (!await db.Players.AnyAsync(p => p.Id == request.PlayerId, ct))
                return ApiHelpers.BadRequest("The selected member does not exist.");

            entity.Title = request.Title.Trim();
            entity.PlayerId = request.PlayerId;
            entity.OpposingClubId = request.OpposingClubId;
            entity.Priority = request.Priority;
            entity.LawyerName = request.LawyerName;
            entity.LawyerFirm = request.LawyerFirm;
            entity.AssignedCounselId = request.AssignedCounselId;
            entity.ClaimAmount = request.ClaimAmount;
            entity.AwardedAmount = request.AwardedAmount;
            entity.Outcome = request.Outcome;
            entity.HearingDate = request.HearingDate;
            entity.Description = request.Description;
            await db.SaveChangesAsync(ct);

            var updated = await LoadDetail(db, id, ct);
            return Results.Ok(ToDetail(updated!));
        })
        .RequireAuthorization(Policies.LegalWrite)
        .WithName("UpdateLegalCase");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<LegalStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid legal status.");

            var entity = await db.LegalCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Legal case not found.");
            if (!current.CanWriteDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this department's cases.");
            if (target == LegalStatus.Closed && !current.CanApproveDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can close a matter.");

            if (!WorkflowRules.CanTransition(WorkflowRules.Legal, entity.Status, target))
                return ApiHelpers.Conflict(
                    $"A matter cannot move from {entity.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.Legal, entity.Status))}.");

            var previous = entity.Status;
            entity.Status = target;
            if (target == LegalStatus.DecisionReceived) entity.DecisionDate = DateTime.UtcNow;
            if (target == LegalStatus.Closed) entity.ClosedAt = DateTime.UtcNow;

            db.LegalCaseEvents.Add(new LegalCaseEvent
            {
                LegalCaseId = entity.Id,
                Title = $"Status: {target}",
                Detail = string.IsNullOrWhiteSpace(request.Comment)
                    ? $"Moved from {previous} to {target}."
                    : request.Comment,
                OccurredAt = DateTime.UtcNow,
                StatusAtEvent = target,
                AuthorId = current.UserId
            });
            await db.SaveChangesAsync(ct);

            var updated = await LoadDetail(db, id, ct);
            return Results.Ok(ToDetail(updated!));
        })
        .RequireAuthorization(Policies.LegalWrite)
        .WithName("TransitionLegalCase");

        group.MapPost("/{id:guid}/events", async (
            Guid id, [FromBody] AddLegalEventRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var entity = await db.LegalCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Legal case not found.");
            if (!current.CanWriteDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("You cannot add events to this department's cases.");

            var evt = new LegalCaseEvent
            {
                LegalCaseId = id,
                Title = request.Title.Trim(),
                Detail = request.Detail,
                OccurredAt = request.OccurredAt ?? DateTime.UtcNow,
                AuthorId = current.UserId
            };
            db.LegalCaseEvents.Add(evt);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/legal/cases/{id}",
                new LegalEventDto(evt.Id, evt.Title, evt.Detail, evt.OccurredAt, null, current.UserName));
        })
        .RequireAuthorization(Policies.LegalWrite)
        .WithName("AddLegalEvent");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var entity = await db.LegalCases.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null) return ApiHelpers.NotFoundProblem("Legal case not found.");
            if (!current.CanApproveDepartment(entity.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can delete a matter.");

            db.LegalCases.Remove(entity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.LegalApprove)
        .WithName("DeleteLegalCase");
    }

    private static Task<LegalCase?> LoadDetail(AppDbContext db, Guid id, CancellationToken ct) =>
        db.LegalCases.AsNoTracking()
            .Include(c => c.Player)
            .Include(c => c.OpposingClub)
            .Include(c => c.Department)
            .Include(c => c.AssignedCounsel)
            .Include(c => c.Events.OrderByDescending(e => e.OccurredAt)).ThenInclude(e => e.Author)
            .Include(c => c.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private static LegalCaseDetailDto ToDetail(LegalCase c) => new(
        c.Id, c.CaseNumber, c.Title, c.Description,
        c.PlayerId, c.Player.FullName, c.OpposingClubId, c.OpposingClub?.Name,
        c.DepartmentId, c.Department.Name, c.Type, c.Status, c.Outcome, c.Priority,
        c.LawyerName, c.LawyerFirm, c.AssignedCounselId, c.AssignedCounsel?.FullName,
        c.ClaimAmount, c.AwardedAmount, c.Currency,
        c.FiledAt, c.HearingDate, c.DecisionDate, c.ClosedAt,
        c.Events.Select(e => new LegalEventDto(e.Id, e.Title, e.Detail, e.OccurredAt,
            e.StatusAtEvent?.ToString(), e.Author?.FullName)).ToList(),
        c.Documents.Select(DocumentEndpoints.ToListDto).ToList(),
        WorkflowRules.Next(WorkflowRules.Legal, c.Status).Select(s => s.ToString()).ToList());
}
