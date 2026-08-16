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

public static class GovernanceEndpoints
{
    public static void MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        MapMeetings(app);
        MapMotions(app);
    }

    private static void MapMeetings(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/meetings").WithTags("Governance").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] MeetingStatus? status,
            [FromQuery] MeetingType? type,
            [FromQuery] bool? upcoming,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.Meetings.AsNoTracking()
                .Include(m => m.Chair)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(m => m.Title.Contains(term) || m.ReferenceNumber.Contains(term));
            }
            if (status is { } s) query = query.Where(m => m.Status == s);
            if (type is { } t) query = query.Where(m => m.Type == t);
            if (upcoming == true) query = query.Where(m => m.ScheduledAt >= DateTime.UtcNow);
            if (upcoming == false) query = query.Where(m => m.ScheduledAt < DateTime.UtcNow);

            query = page.SortDescending
                ? query.OrderByDescending(m => m.ScheduledAt)
                : query.OrderBy(m => m.ScheduledAt);

            var result = await query
                .Select(m => new MeetingListDto(m.Id, m.ReferenceNumber, m.Title, m.Type, m.Status,
                    m.ScheduledAt, m.DurationMinutes, m.Location,
                    m.Chair != null ? m.Chair.FullName : null,
                    m.Attendees.Count, m.Motions.Count))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.GovernanceRead)
        .WithName("ListMeetings");

        group.MapGet("/participation", async (AppDbContext db, CancellationToken ct) =>
        {
            // Voting participation per month: votes cast divided by eligible voters on closed motions.
            var now = DateTime.UtcNow;
            var points = new List<ParticipationPoint>();

            for (var back = 5; back >= 0; back--)
            {
                var point = now.AddMonths(-back);
                var monthStart = new DateTime(point.Year, point.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);

                var motions = await db.Motions.AsNoTracking()
                    .Include(m => m.Meeting).ThenInclude(mm => mm.Attendees)
                    .Include(m => m.Votes)
                    .Where(m => m.VotingOpensAt >= monthStart && m.VotingOpensAt < monthEnd
                                && (m.Status == MotionStatus.Passed || m.Status == MotionStatus.Failed))
                    .ToListAsync(ct);

                var eligible = motions.Sum(m => m.Meeting.Attendees.Count(a => a.IsVotingMember));
                var cast = motions.Sum(m => m.Votes.Count);
                var rate = eligible == 0 ? 0d : Math.Round(cast / (double)eligible * 100, 1);

                points.Add(new ParticipationPoint(
                    System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat
                        .GetAbbreviatedMonthName(point.Month),
                    rate, motions.Count));
            }

            return Results.Ok(points);
        })
        .RequireAuthorization(Policies.GovernanceRead)
        .WithName("VotingParticipation");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var meeting = await LoadMeeting(db, id, ct);
            if (meeting is null) return ApiHelpers.NotFoundProblem("Meeting not found.");
            if (!current.CanReadDepartment(meeting.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this meeting.");
            return Results.Ok(ToDetail(meeting, current));
        })
        .RequireAuthorization(Policies.GovernanceRead)
        .WithName("GetMeeting");

        group.MapPost("/", async (
            [FromBody] MeetingUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var dept = await db.Departments.FirstOrDefaultAsync(d => d.Code == DepartmentCodes.Governance, ct);
            if (dept is null) return ApiHelpers.BadRequest("The governance department is not configured.");
            if (!current.CanWriteDepartment(dept.Id))
                return ApiHelpers.Forbidden("You cannot schedule meetings.");

            var meeting = new Meeting
            {
                ReferenceNumber = await refs.NextMeetingAsync(ct),
                DepartmentId = dept.Id,
                Title = request.Title.Trim(),
                Type = request.Type,
                Status = MeetingStatus.Scheduled,
                ScheduledAt = request.ScheduledAt,
                DurationMinutes = request.DurationMinutes,
                Location = request.Location,
                VideoLink = request.VideoLink,
                Agenda = request.Agenda,
                QuorumRequired = request.QuorumRequired,
                ChairId = request.ChairId
            };
            db.Meetings.Add(meeting);

            foreach (var userId in (request.AttendeeUserIds ?? []).Distinct())
            {
                if (!await db.ActiveUsers.AnyAsync(u => u.Id == userId, ct)) continue;
                db.MeetingAttendees.Add(new MeetingAttendee { MeetingId = meeting.Id, UserId = userId });
            }
            await db.SaveChangesAsync(ct);

            var created = await LoadMeeting(db, meeting.Id, ct);
            return Results.Created($"/api/meetings/{meeting.Id}", ToDetail(created!, current));
        })
        .RequireAuthorization(Policies.GovernanceWrite)
        .WithName("CreateMeeting");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] MeetingUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var meeting = await db.Meetings.Include(m => m.Attendees)
                .FirstOrDefaultAsync(m => m.Id == id, ct);
            if (meeting is null) return ApiHelpers.NotFoundProblem("Meeting not found.");
            if (!current.CanWriteDepartment(meeting.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this meeting.");
            if (meeting.Status == MeetingStatus.Completed)
                return ApiHelpers.Conflict("A completed meeting cannot be edited.");

            meeting.Title = request.Title.Trim();
            meeting.Type = request.Type;
            meeting.ScheduledAt = request.ScheduledAt;
            meeting.DurationMinutes = request.DurationMinutes;
            meeting.Location = request.Location;
            meeting.VideoLink = request.VideoLink;
            meeting.Agenda = request.Agenda;
            meeting.Minutes = request.Minutes;
            meeting.QuorumRequired = request.QuorumRequired;
            meeting.ChairId = request.ChairId;

            if (request.AttendeeUserIds is not null)
            {
                var desired = request.AttendeeUserIds.Distinct().ToHashSet();
                var existing = meeting.Attendees.ToList();

                foreach (var attendee in existing.Where(a => !desired.Contains(a.UserId)))
                    db.MeetingAttendees.Remove(attendee);

                var already = existing.Select(a => a.UserId).ToHashSet();
                foreach (var userId in desired.Where(u => !already.Contains(u)))
                {
                    if (!await db.ActiveUsers.AnyAsync(u => u.Id == userId, ct)) continue;
                    db.MeetingAttendees.Add(new MeetingAttendee { MeetingId = id, UserId = userId });
                }
            }

            await db.SaveChangesAsync(ct);
            var updated = await LoadMeeting(db, id, ct);
            return Results.Ok(ToDetail(updated!, current));
        })
        .RequireAuthorization(Policies.GovernanceWrite)
        .WithName("UpdateMeeting");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<MeetingStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid meeting status.");

            var meeting = await db.Meetings.Include(m => m.Motions)
                .FirstOrDefaultAsync(m => m.Id == id, ct);
            if (meeting is null) return ApiHelpers.NotFoundProblem("Meeting not found.");
            if (!current.CanWriteDepartment(meeting.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this meeting.");

            if (!WorkflowRules.CanTransition(WorkflowRules.Meeting, meeting.Status, target))
                return ApiHelpers.Conflict(
                    $"A meeting cannot move from {meeting.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.Meeting, meeting.Status))}.");

            if (target == MeetingStatus.Completed && meeting.Motions.Any(m => m.Status == MotionStatus.Open))
                return ApiHelpers.Conflict("Close or withdraw every open motion before completing the meeting.");

            meeting.Status = target;
            await db.SaveChangesAsync(ct);

            var updated = await LoadMeeting(db, id, ct);
            return Results.Ok(ToDetail(updated!, current));
        })
        .RequireAuthorization(Policies.GovernanceWrite)
        .WithName("TransitionMeeting");

        group.MapPost("/{id:guid}/attendance", async (
            Guid id, [FromBody] AttendanceRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (current.UserId is not { } userId) return Results.Unauthorized();

            var attendee = await db.MeetingAttendees
                .FirstOrDefaultAsync(a => a.MeetingId == id && a.UserId == userId, ct);
            if (attendee is null) return ApiHelpers.Forbidden("You are not invited to this meeting.");

            attendee.Status = request.Status;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.GovernanceRead)
        .WithName("SetOwnAttendance");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (meeting is null) return ApiHelpers.NotFoundProblem("Meeting not found.");
            if (!current.CanApproveDepartment(meeting.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can delete a meeting.");
            if (meeting.Status == MeetingStatus.Completed)
                return ApiHelpers.Conflict("A completed meeting is part of the record and cannot be deleted.");

            db.Meetings.Remove(meeting);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.GovernanceApprove)
        .WithName("DeleteMeeting");
    }

    private static void MapMotions(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/motions").WithTags("Governance").RequireAuthorization();

        group.MapPost("/meeting/{meetingId:guid}", async (
            Guid meetingId, [FromBody] MotionUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var meeting = await db.Meetings.Include(m => m.Motions)
                .FirstOrDefaultAsync(m => m.Id == meetingId, ct);
            if (meeting is null) return ApiHelpers.NotFoundProblem("Meeting not found.");
            if (!current.CanWriteDepartment(meeting.DepartmentId))
                return ApiHelpers.Forbidden("You cannot add motions to this meeting.");
            if (meeting.Status == MeetingStatus.Completed)
                return ApiHelpers.Conflict("Motions cannot be added to a completed meeting.");

            var motion = new Motion
            {
                MeetingId = meetingId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Status = MotionStatus.Draft,
                SequenceNumber = meeting.Motions.Count + 1,
                VotingOpensAt = request.VotingOpensAt,
                VotingClosesAt = request.VotingClosesAt,
                PassThreshold = request.PassThreshold,
                IsSecretBallot = request.IsSecretBallot
            };
            db.Motions.Add(motion);
            await db.SaveChangesAsync(ct);

            var saved = await LoadMotion(db, motion.Id, ct);
            return Results.Created($"/api/motions/{motion.Id}", ToMotionDto(saved!, current));
        })
        .RequireAuthorization(Policies.GovernanceWrite)
        .WithName("CreateMotion");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var motion = await LoadMotion(db, id, ct);
            if (motion is null) return ApiHelpers.NotFoundProblem("Motion not found.");
            if (!current.CanReadDepartment(motion.Meeting.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this motion.");
            return Results.Ok(ToMotionDto(motion, current));
        })
        .RequireAuthorization(Policies.GovernanceRead)
        .WithName("GetMotion");

        group.MapPost("/{id:guid}/open", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var motion = await db.Motions.Include(m => m.Meeting)
                .FirstOrDefaultAsync(m => m.Id == id, ct);
            if (motion is null) return ApiHelpers.NotFoundProblem("Motion not found.");
            if (!current.CanWriteDepartment(motion.Meeting.DepartmentId))
                return ApiHelpers.Forbidden("You cannot open voting on this motion.");
            if (!WorkflowRules.CanTransition(WorkflowRules.Motion, motion.Status, MotionStatus.Open))
                return ApiHelpers.Conflict($"A motion in {motion.Status} status cannot be opened.");

            motion.Status = MotionStatus.Open;
            motion.VotingOpensAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var saved = await LoadMotion(db, id, ct);
            return Results.Ok(ToMotionDto(saved!, current));
        })
        .RequireAuthorization(Policies.GovernanceWrite)
        .WithName("OpenMotionVoting");

        group.MapPost("/{id:guid}/vote", async (
            Guid id, [FromBody] CastVoteRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (current.UserId is not { } userId) return Results.Unauthorized();

            var motion = await db.Motions.Include(m => m.Meeting)
                .FirstOrDefaultAsync(m => m.Id == id, ct);
            if (motion is null) return ApiHelpers.NotFoundProblem("Motion not found.");
            if (motion.Status != MotionStatus.Open)
                return ApiHelpers.Conflict("Voting is not open on this motion.");
            if (motion.VotingClosesAt is { } closes && closes < DateTime.UtcNow)
                return ApiHelpers.Conflict("The voting window for this motion has closed.");

            // Only invited voting members of the meeting may vote.
            var attendee = await db.MeetingAttendees
                .FirstOrDefaultAsync(a => a.MeetingId == motion.MeetingId && a.UserId == userId, ct);
            if (attendee is null || !attendee.IsVotingMember)
                return ApiHelpers.Forbidden("You are not a voting member of this meeting.");

            var existing = await db.Votes.FirstOrDefaultAsync(v => v.MotionId == id && v.UserId == userId, ct);
            if (existing is not null)
            {
                // Members may change their vote while the motion remains open.
                existing.Choice = request.Choice;
                existing.CastAt = DateTime.UtcNow;
            }
            else
            {
                db.Votes.Add(new Vote { MotionId = id, UserId = userId, Choice = request.Choice });
            }
            await db.SaveChangesAsync(ct);

            var saved = await LoadMotion(db, id, ct);
            return Results.Ok(ToMotionDto(saved!, current));
        })
        .RequireAuthorization(Policies.GovernanceRead)
        .WithName("CastVote");

        group.MapPost("/{id:guid}/close", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var motion = await LoadMotion(db, id, ct);
            if (motion is null) return ApiHelpers.NotFoundProblem("Motion not found.");
            if (!current.CanApproveDepartment(motion.Meeting.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can close voting.");
            if (motion.Status != MotionStatus.Open)
                return ApiHelpers.Conflict($"A motion in {motion.Status} status cannot be closed.");

            var votingMembers = motion.Meeting.Attendees.Count(a => a.IsVotingMember);
            var attended = motion.Meeting.Attendees.Count(
                a => a.Status is AttendeeStatus.Attended or AttendeeStatus.Accepted);

            // Quorum is measured against the meeting's requirement, not simply votes cast.
            if (Math.Max(attended, motion.Votes.Count) < motion.Meeting.QuorumRequired)
                return ApiHelpers.Conflict(
                    $"Quorum not met: {motion.Meeting.QuorumRequired} members required, " +
                    $"{Math.Max(attended, motion.Votes.Count)} present.");

            var votesFor = motion.Votes.Count(v => v.Choice == VoteChoice.For);
            var votesAgainst = motion.Votes.Count(v => v.Choice == VoteChoice.Against);
            var decisive = votesFor + votesAgainst;

            // Abstentions do not count toward the threshold.
            var passed = decisive > 0 && votesFor / (double)decisive > motion.PassThreshold - 1e-9;

            var tracked = await db.Motions.FirstAsync(m => m.Id == id, ct);
            tracked.Status = passed ? MotionStatus.Passed : MotionStatus.Failed;
            tracked.VotingClosesAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var saved = await LoadMotion(db, id, ct);
            return Results.Ok(ToMotionDto(saved!, current));
        })
        .RequireAuthorization(Policies.GovernanceApprove)
        .WithName("CloseMotionVoting");

        group.MapPost("/{id:guid}/withdraw", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var motion = await db.Motions.Include(m => m.Meeting).FirstOrDefaultAsync(m => m.Id == id, ct);
            if (motion is null) return ApiHelpers.NotFoundProblem("Motion not found.");
            if (!current.CanWriteDepartment(motion.Meeting.DepartmentId))
                return ApiHelpers.Forbidden("You cannot withdraw this motion.");
            if (!WorkflowRules.CanTransition(WorkflowRules.Motion, motion.Status, MotionStatus.Withdrawn))
                return ApiHelpers.Conflict($"A motion in {motion.Status} status cannot be withdrawn.");

            motion.Status = MotionStatus.Withdrawn;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.GovernanceWrite)
        .WithName("WithdrawMotion");
    }

    // ------------------------------------------------------------------ helpers
    private static Task<Meeting?> LoadMeeting(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Meetings.AsNoTracking()
            .Include(m => m.Department)
            .Include(m => m.Chair)
            .Include(m => m.Attendees).ThenInclude(a => a.User).ThenInclude(u => u!.Department)
            .Include(m => m.Motions).ThenInclude(mo => mo.Votes).ThenInclude(v => v.User)
            .Include(m => m.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    private static Task<Motion?> LoadMotion(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Motions.AsNoTracking()
            .Include(m => m.Meeting).ThenInclude(mm => mm.Attendees)
            .Include(m => m.Votes).ThenInclude(v => v.User)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    private static MeetingDetailDto ToDetail(Meeting m, ICurrentUser current)
    {
        var attended = m.Attendees.Count(a => a.Status is AttendeeStatus.Attended or AttendeeStatus.Accepted);

        return new MeetingDetailDto(
            m.Id, m.ReferenceNumber, m.Title, m.Type, m.Status, m.ScheduledAt, m.DurationMinutes,
            m.Location, m.VideoLink, m.Agenda, m.Minutes, m.QuorumRequired,
            m.DepartmentId, m.Department.Name, m.ChairId, m.Chair?.FullName,
            m.Attendees.Select(a => new AttendeeDto(a.Id, a.UserId,
                a.User?.FullName ?? "Unknown", a.User?.Department?.Name, a.Status, a.IsVotingMember))
                .OrderBy(a => a.UserName).ToList(),
            m.Motions.OrderBy(mo => mo.SequenceNumber)
                .Select(mo => ToMotionDto(mo, current, m)).ToList(),
            m.Documents.Select(DocumentEndpoints.ToListDto).ToList(),
            attended >= m.QuorumRequired,
            WorkflowRules.Next(WorkflowRules.Meeting, m.Status).Select(s => s.ToString()).ToList());
    }

    private static MotionDto ToMotionDto(Motion mo, ICurrentUser current, Meeting? meeting = null)
    {
        meeting ??= mo.Meeting;
        var myVote = current.UserId is { } uid
            ? mo.Votes.FirstOrDefault(v => v.UserId == uid)?.Choice
            : null;

        var isVotingMember = current.UserId is { } id
            && (meeting?.Attendees.Any(a => a.UserId == id && a.IsVotingMember) ?? false);

        // A secret ballot hides individual choices from everyone but a Super Admin.
        var visibleVotes = mo.IsSecretBallot && !current.IsSuperAdmin
            ? []
            : mo.Votes.Select(v => new VoteDto(v.Id, v.UserId,
                v.User?.FullName ?? "Unknown", v.Choice, v.CastAt)).ToList();

        return new MotionDto(
            mo.Id, mo.MeetingId, mo.Title, mo.Description, mo.Status, mo.SequenceNumber,
            mo.VotingOpensAt, mo.VotingClosesAt, mo.PassThreshold, mo.IsSecretBallot,
            mo.Votes.Count(v => v.Choice == VoteChoice.For),
            mo.Votes.Count(v => v.Choice == VoteChoice.Against),
            mo.Votes.Count(v => v.Choice == VoteChoice.Abstain),
            meeting?.Attendees.Count(a => a.IsVotingMember) ?? 0,
            myVote,
            mo.Status == MotionStatus.Open && isVotingMember,
            visibleVotes);
    }
}
