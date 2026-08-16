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

public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events").WithTags("Operations").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] EventStatus? status,
            [FromQuery] EventType? type,
            [FromQuery] string? city,
            [FromQuery] bool? upcoming,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.Events.AsNoTracking().Include(e => e.Owner).WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(e => e.Name.Contains(term) || e.ReferenceNumber.Contains(term));
            }
            if (status is { } s) query = query.Where(e => e.Status == s);
            if (type is { } t) query = query.Where(e => e.Type == t);
            if (!string.IsNullOrWhiteSpace(city)) query = query.Where(e => e.City == city);
            if (upcoming == true) query = query.Where(e => e.StartDate >= DateTime.UtcNow);
            if (upcoming == false) query = query.Where(e => e.StartDate < DateTime.UtcNow);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "name" => page.SortDescending ? query.OrderByDescending(e => e.Name) : query.OrderBy(e => e.Name),
                "budget" => page.SortDescending
                    ? query.OrderByDescending(e => e.BudgetAmount) : query.OrderBy(e => e.BudgetAmount),
                _ => page.SortDescending
                    ? query.OrderByDescending(e => e.StartDate) : query.OrderBy(e => e.StartDate)
            };

            var result = await query
                .Select(e => new EventListDto(e.Id, e.ReferenceNumber, e.Name, e.Type, e.Status,
                    e.StartDate, e.EndDate, e.Venue, e.City, e.BudgetAmount, e.ActualCost,
                    e.ExpectedAttendees, e.ActualAttendees,
                    e.Owner != null ? e.Owner.FullName : null, e.Participants.Count))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.OperationsRead)
        .WithName("ListEvents");

        group.MapGet("/stats", async (AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var scoped = db.Events.AsNoTracking().WhereReadable(current);
            var byType = await scoped.GroupBy(e => e.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(ct);
            var byStatus = await scoped.GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);

            return Results.Ok(new
            {
                Total = await scoped.CountAsync(ct),
                Upcoming = await scoped.CountAsync(e => e.StartDate >= DateTime.UtcNow, ct),
                Completed = await scoped.CountAsync(e => e.Status == EventStatus.Completed, ct),
                TotalBudget = await scoped.SumAsync(e => e.BudgetAmount, ct),
                TotalSpent = await scoped.SumAsync(e => e.ActualCost, ct),
                ByType = byType.Select(x => new CountByLabel(x.Type.ToString(), x.Count)).ToList(),
                ByStatus = byStatus.Select(x => new CountByLabel(x.Status.ToString(), x.Count)).ToList()
            });
        })
        .RequireAuthorization(Policies.OperationsRead)
        .WithName("EventStats");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var evt = await LoadEvent(db, id, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanReadDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this event.");
            return Results.Ok(ToDetail(evt));
        })
        .RequireAuthorization(Policies.OperationsRead)
        .WithName("GetEvent");

        group.MapPost("/", async (
            [FromBody] EventUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (request.EndDate is { } end && end < request.StartDate)
                return ApiHelpers.BadRequest("The end date cannot be before the start date.");

            var dept = await db.Departments.FirstOrDefaultAsync(d => d.Code == DepartmentCodes.Operations, ct);
            if (dept is null) return ApiHelpers.BadRequest("The operations department is not configured.");
            if (!current.CanWriteDepartment(dept.Id))
                return ApiHelpers.Forbidden("You cannot create events.");

            var evt = new Event
            {
                ReferenceNumber = await refs.NextEventAsync(ct),
                DepartmentId = dept.Id,
                Name = request.Name.Trim(),
                Type = request.Type,
                Status = EventStatus.Planned,
                Description = request.Description,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Venue = request.Venue,
                City = request.City,
                BudgetAmount = request.BudgetAmount,
                ActualCost = request.ActualCost,
                ExpectedAttendees = request.ExpectedAttendees,
                ActualAttendees = request.ActualAttendees,
                OwnerId = request.OwnerId ?? current.UserId
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync(ct);

            var created = await LoadEvent(db, evt.Id, ct);
            return Results.Created($"/api/events/{evt.Id}", ToDetail(created!));
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("CreateEvent");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] EventUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (request.EndDate is { } end && end < request.StartDate)
                return ApiHelpers.BadRequest("The end date cannot be before the start date.");

            var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanWriteDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this event.");

            evt.Name = request.Name.Trim();
            evt.Type = request.Type;
            evt.Description = request.Description;
            evt.StartDate = request.StartDate;
            evt.EndDate = request.EndDate;
            evt.Venue = request.Venue;
            evt.City = request.City;
            evt.BudgetAmount = request.BudgetAmount;
            evt.ActualCost = request.ActualCost;
            evt.ExpectedAttendees = request.ExpectedAttendees;
            evt.ActualAttendees = request.ActualAttendees;
            evt.OwnerId = request.OwnerId;
            await db.SaveChangesAsync(ct);

            var updated = await LoadEvent(db, id, ct);
            return Results.Ok(ToDetail(updated!));
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("UpdateEvent");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<EventStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid event status.");

            var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanWriteDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this event.");

            if (!WorkflowRules.CanTransition(WorkflowRules.Event, evt.Status, target))
                return ApiHelpers.Conflict(
                    $"An event cannot move from {evt.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.Event, evt.Status))}.");

            evt.Status = target;
            await db.SaveChangesAsync(ct);

            var updated = await LoadEvent(db, id, ct);
            return Results.Ok(ToDetail(updated!));
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("TransitionEvent");

        group.MapPost("/{id:guid}/participants", async (
            Guid id, [FromBody] AddParticipantsRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanWriteDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this event.");

            var already = await db.EventParticipants.Where(p => p.EventId == id)
                .Select(p => p.PlayerId).ToListAsync(ct);

            var added = 0;
            foreach (var playerId in request.PlayerIds.Distinct().Where(p => !already.Contains(p)))
            {
                if (!await db.Players.AnyAsync(p => p.Id == playerId, ct)) continue;
                db.EventParticipants.Add(new EventParticipant { EventId = id, PlayerId = playerId });
                added++;
            }
            await db.SaveChangesAsync(ct);

            var updated = await LoadEvent(db, id, ct);
            return Results.Ok(new { added, evt = ToDetail(updated!) });
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("AddEventParticipants");

        group.MapDelete("/{eventId:guid}/participants/{participantId:guid}", async (
            Guid eventId, Guid participantId, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanWriteDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this event.");

            var participant = await db.EventParticipants
                .FirstOrDefaultAsync(p => p.Id == participantId && p.EventId == eventId, ct);
            if (participant is null) return ApiHelpers.NotFoundProblem("Participant not found.");

            db.EventParticipants.Remove(participant);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("RemoveEventParticipant");

        group.MapPost("/{eventId:guid}/participants/{participantId:guid}/attendance", async (
            Guid eventId, Guid participantId, [FromBody] AttendanceRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanWriteDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this event.");

            var participant = await db.EventParticipants
                .FirstOrDefaultAsync(p => p.Id == participantId && p.EventId == eventId, ct);
            if (participant is null) return ApiHelpers.NotFoundProblem("Participant not found.");

            participant.Status = request.Status;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("SetParticipantAttendance");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (evt is null) return ApiHelpers.NotFoundProblem("Event not found.");
            if (!current.CanApproveDepartment(evt.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can delete an event.");

            db.Events.Remove(evt);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.OperationsWrite)
        .WithName("DeleteEvent");
    }

    private static Task<Event?> LoadEvent(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Events.AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.Owner)
            .Include(e => e.Participants).ThenInclude(p => p.Player).ThenInclude(pl => pl.CurrentClub)
            .Include(e => e.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    private static EventDetailDto ToDetail(Event e) => new(
        e.Id, e.ReferenceNumber, e.Name, e.Type, e.Status, e.Description,
        e.StartDate, e.EndDate, e.Venue, e.City, e.BudgetAmount, e.ActualCost,
        e.ExpectedAttendees, e.ActualAttendees, e.DepartmentId, e.Department.Name,
        e.OwnerId, e.Owner?.FullName,
        e.Participants.Select(p => new EventParticipantDto(p.Id, p.PlayerId,
            p.Player.FullName, p.Player.CurrentClub?.Name, p.Status, p.Notes))
            .OrderBy(p => p.PlayerName).ToList(),
        e.Documents.Select(DocumentEndpoints.ToListDto).ToList(),
        WorkflowRules.Next(WorkflowRules.Event, e.Status).Select(s => s.ToString()).ToList());
}
