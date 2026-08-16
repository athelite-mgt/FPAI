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

public static class WorkEndpoints
{
    public static void MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        MapTasks(app);
        MapApprovals(app);
        MapNotifications(app);
    }

    private static void MapTasks(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").WithTags("Tasks").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] WorkTaskStatus? status,
            [FromQuery] CasePriority? priority,
            [FromQuery] Guid? assigneeId,
            [FromQuery] Guid? departmentId,
            [FromQuery] bool? mine,
            [FromQuery] bool? overdue,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.WorkTasks.AsNoTracking()
                .Include(t => t.Department).Include(t => t.Assignee)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(t => t.Title.Contains(term) || t.ReferenceNumber.Contains(term));
            }
            if (status is { } s) query = query.Where(t => t.Status == s);
            if (priority is { } p) query = query.Where(t => t.Priority == p);
            if (assigneeId is { } a) query = query.Where(t => t.AssigneeId == a);
            if (departmentId is { } d) query = query.Where(t => t.DepartmentId == d);
            if (mine == true && current.UserId is { } me) query = query.Where(t => t.AssigneeId == me);
            if (overdue == true)
                query = query.Where(t => t.DueDate != null
                                         && t.DueDate < DateTime.UtcNow
                                         && t.Status != WorkTaskStatus.Done
                                         && t.Status != WorkTaskStatus.Cancelled);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "duedate" => page.SortDescending
                    ? query.OrderByDescending(t => t.DueDate) : query.OrderBy(t => t.DueDate),
                "priority" => page.SortDescending
                    ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
                "status" => page.SortDescending
                    ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
                _ => page.SortDescending
                    ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt)
            };

            var result = await query.ToPagedResultAsync(page, ToTaskDto, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.TasksRead)
        .WithName("ListTasks");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var task = await db.WorkTasks.AsNoTracking()
                .Include(t => t.Department).Include(t => t.Assignee)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return ApiHelpers.NotFoundProblem("Task not found.");
            if (!current.CanReadDepartment(task.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this task.");
            return Results.Ok(ToTaskDto(task));
        })
        .RequireAuthorization(Policies.TasksRead)
        .WithName("GetTask");

        group.MapPost("/", async (
            [FromBody] WorkTaskUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!current.CanWriteDepartment(request.DepartmentId))
                return ApiHelpers.Forbidden("You cannot create tasks for this department.");
            if (!await db.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");
            if (request.AssigneeId is { } assignee && !await db.ActiveUsers.AnyAsync(u => u.Id == assignee, ct))
                return ApiHelpers.BadRequest("The selected assignee does not exist.");

            var task = new WorkTask
            {
                ReferenceNumber = await refs.NextTaskAsync(ct),
                DepartmentId = request.DepartmentId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Status = WorkTaskStatus.Todo,
                Priority = request.Priority,
                AssigneeId = request.AssigneeId,
                DueDate = request.DueDate,
                RelatedEntityType = request.RelatedEntityType,
                RelatedEntityId = request.RelatedEntityId
            };
            db.WorkTasks.Add(task);

            if (request.AssigneeId is { } notify && notify != current.UserId)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = notify,
                    Title = "A task was assigned to you",
                    Body = task.Title,
                    Link = $"/tasks/{task.Id}"
                });
            }
            await db.SaveChangesAsync(ct);

            var saved = await db.WorkTasks.AsNoTracking()
                .Include(t => t.Department).Include(t => t.Assignee)
                .FirstAsync(t => t.Id == task.Id, ct);
            return Results.Created($"/api/tasks/{task.Id}", ToTaskDto(saved));
        })
        .RequireAuthorization(Policies.TasksWrite)
        .WithName("CreateTask");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] WorkTaskUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return ApiHelpers.NotFoundProblem("Task not found.");
            if (!current.CanWriteDepartment(task.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this task.");

            var previousAssignee = task.AssigneeId;
            task.Title = request.Title.Trim();
            task.Description = request.Description;
            task.Priority = request.Priority;
            task.AssigneeId = request.AssigneeId;
            task.DueDate = request.DueDate;
            task.RelatedEntityType = request.RelatedEntityType;
            task.RelatedEntityId = request.RelatedEntityId;

            if (request.AssigneeId is { } newAssignee
                && newAssignee != previousAssignee && newAssignee != current.UserId)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = newAssignee,
                    Title = "A task was assigned to you",
                    Body = task.Title,
                    Link = $"/tasks/{task.Id}"
                });
            }
            await db.SaveChangesAsync(ct);

            var saved = await db.WorkTasks.AsNoTracking()
                .Include(t => t.Department).Include(t => t.Assignee)
                .FirstAsync(t => t.Id == id, ct);
            return Results.Ok(ToTaskDto(saved));
        })
        .RequireAuthorization(Policies.TasksWrite)
        .WithName("UpdateTask");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<WorkTaskStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid task status.");

            var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return ApiHelpers.NotFoundProblem("Task not found.");

            // The assignee can always progress their own task, even across departments.
            var isAssignee = current.UserId is { } uid && task.AssigneeId == uid;
            if (!isAssignee && !current.CanWriteDepartment(task.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this task.");

            if (!WorkflowRules.CanTransition(WorkflowRules.WorkTask, task.Status, target))
                return ApiHelpers.Conflict(
                    $"A task cannot move from {task.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.WorkTask, task.Status))}.");

            task.Status = target;
            task.CompletedAt = target == WorkTaskStatus.Done ? DateTime.UtcNow : null;
            await db.SaveChangesAsync(ct);

            var saved = await db.WorkTasks.AsNoTracking()
                .Include(t => t.Department).Include(t => t.Assignee)
                .FirstAsync(t => t.Id == id, ct);
            return Results.Ok(ToTaskDto(saved));
        })
        .RequireAuthorization(Policies.TasksRead)
        .WithName("TransitionTask");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return ApiHelpers.NotFoundProblem("Task not found.");
            if (!current.CanWriteDepartment(task.DepartmentId))
                return ApiHelpers.Forbidden("You cannot delete this task.");

            db.WorkTasks.Remove(task);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.TasksWrite)
        .WithName("DeleteTask");
    }

    private static void MapApprovals(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approvals").WithTags("Approvals").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] ApprovalStatus? status,
            [FromQuery] Guid? departmentId,
            [FromQuery] bool? awaitingMe,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.ApprovalRequests.AsNoTracking()
                .Include(a => a.Department).Include(a => a.RequestedBy).Include(a => a.DecidedBy)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(a => a.Title.Contains(term) || a.ReferenceNumber.Contains(term));
            }
            if (status is { } s) query = query.Where(a => a.Status == s);
            if (departmentId is { } d) query = query.Where(a => a.DepartmentId == d);

            if (awaitingMe == true)
            {
                query = query.Where(a => a.Status == ApprovalStatus.Pending);
                // Only items this caller could actually decide.
                if (!current.IsSuperAdmin)
                {
                    var dept = current.DepartmentId;
                    query = current.IsInRole(RoleNames.DepartmentHead) && dept is not null
                        ? query.Where(a => a.DepartmentId == dept)
                        : query.Where(_ => false);
                }
            }

            query = page.SortDescending
                ? query.OrderByDescending(a => a.CreatedAt)
                : query.OrderBy(a => a.CreatedAt);

            var result = await query.ToPagedResultAsync(page, a => ToApprovalDto(a, current), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.TasksRead)
        .WithName("ListApprovals");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var approval = await db.ApprovalRequests.AsNoTracking()
                .Include(a => a.Department).Include(a => a.RequestedBy).Include(a => a.DecidedBy)
                .FirstOrDefaultAsync(a => a.Id == id, ct);
            if (approval is null) return ApiHelpers.NotFoundProblem("Approval request not found.");
            if (!current.CanReadDepartment(approval.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this request.");
            return Results.Ok(ToApprovalDto(approval, current));
        })
        .RequireAuthorization(Policies.TasksRead)
        .WithName("GetApproval");

        group.MapPost("/", async (
            [FromBody] CreateApprovalRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!current.CanWriteDepartment(request.DepartmentId))
                return ApiHelpers.Forbidden("You cannot raise approvals for this department.");
            if (!await db.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");

            var duplicate = await db.ApprovalRequests.AnyAsync(
                a => a.EntityType == request.EntityType
                     && a.EntityId == request.EntityId
                     && a.Status == ApprovalStatus.Pending, ct);
            if (duplicate)
                return ApiHelpers.Conflict("An approval for this record is already pending.");

            var approval = new ApprovalRequest
            {
                ReferenceNumber = await refs.NextApprovalAsync(ct),
                DepartmentId = request.DepartmentId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Status = ApprovalStatus.Pending,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                Amount = request.Amount,
                RequestedById = current.UserId
            };
            db.ApprovalRequests.Add(approval);

            // Notify every head of the owning department that a decision is waiting.
            var heads = await db.ActiveUsers.Where(u => u.DepartmentId == request.DepartmentId).ToListAsync(ct);
            foreach (var head in heads.Where(h => h.Id != current.UserId))
            {
                db.Notifications.Add(new Notification
                {
                    UserId = head.Id,
                    Title = "An approval is awaiting your decision",
                    Body = approval.Title,
                    Link = $"/approvals/{approval.Id}"
                });
            }
            await db.SaveChangesAsync(ct);

            var saved = await db.ApprovalRequests.AsNoTracking()
                .Include(a => a.Department).Include(a => a.RequestedBy).Include(a => a.DecidedBy)
                .FirstAsync(a => a.Id == approval.Id, ct);
            return Results.Created($"/api/approvals/{approval.Id}", ToApprovalDto(saved, current));
        })
        .RequireAuthorization(Policies.TasksWrite)
        .WithName("CreateApproval");

        group.MapPost("/{id:guid}/decision", async (
            Guid id, [FromBody] ApprovalDecisionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var approval = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (approval is null) return ApiHelpers.NotFoundProblem("Approval request not found.");
            if (approval.Status != ApprovalStatus.Pending)
                return ApiHelpers.Conflict($"This request was already {approval.Status.ToString().ToLowerInvariant()}.");

            // Single-step approval: the head of the owning department, or a Super Admin.
            if (!current.CanApproveDepartment(approval.DepartmentId))
                return ApiHelpers.Forbidden(
                    "Only the head of the owning department or an administrator can decide this request.");

            // Requesters cannot approve their own submissions.
            if (approval.RequestedById is { } requester && requester == current.UserId && !current.IsSuperAdmin)
                return ApiHelpers.Forbidden("You cannot approve a request you raised yourself.");

            if (!request.Approve && string.IsNullOrWhiteSpace(request.Comment))
                return ApiHelpers.BadRequest("A comment is required when rejecting a request.");

            approval.Status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            approval.DecidedById = current.UserId;
            approval.DecidedAt = DateTime.UtcNow;
            approval.DecisionComment = request.Comment;

            await ApplySideEffectAsync(db, approval, current, ct);

            if (approval.RequestedById is { } notify)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = notify,
                    Title = $"Your request was {approval.Status.ToString().ToLowerInvariant()}",
                    Body = approval.Title,
                    Link = $"/approvals/{approval.Id}"
                });
            }
            await db.SaveChangesAsync(ct);

            var saved = await db.ApprovalRequests.AsNoTracking()
                .Include(a => a.Department).Include(a => a.RequestedBy).Include(a => a.DecidedBy)
                .FirstAsync(a => a.Id == id, ct);
            return Results.Ok(ToApprovalDto(saved, current));
        })
        .RequireAuthorization(Policies.TasksApprove)
        .WithName("DecideApproval");

        group.MapPost("/{id:guid}/cancel", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var approval = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (approval is null) return ApiHelpers.NotFoundProblem("Approval request not found.");
            if (approval.Status != ApprovalStatus.Pending)
                return ApiHelpers.Conflict("Only a pending request can be cancelled.");

            var isRequester = approval.RequestedById == current.UserId;
            if (!isRequester && !current.CanApproveDepartment(approval.DepartmentId))
                return ApiHelpers.Forbidden("Only the requester or a department head can cancel this request.");

            approval.Status = ApprovalStatus.Cancelled;
            approval.DecidedById = current.UserId;
            approval.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.TasksWrite)
        .WithName("CancelApproval");
    }

    /// <summary>
    /// Propagates an approval decision to the record it governs, so approving a voucher
    /// here also moves the voucher itself rather than leaving the two out of step.
    /// </summary>
    private static async Task ApplySideEffectAsync(
        AppDbContext db, ApprovalRequest approval, ICurrentUser current, CancellationToken ct)
    {
        switch (approval.EntityType)
        {
            case "Voucher":
            {
                var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == approval.EntityId, ct);
                if (voucher is null || voucher.Status != VoucherStatus.Pending) return;

                if (approval.Status == ApprovalStatus.Approved)
                {
                    voucher.Status = VoucherStatus.Approved;
                    voucher.ApprovedById = current.UserId;
                    voucher.ApprovedAt = DateTime.UtcNow;
                }
                else
                {
                    voucher.Status = VoucherStatus.Rejected;
                    voucher.RejectionReason = approval.DecisionComment;
                }
                break;
            }
            case "Expense":
            {
                var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == approval.EntityId, ct);
                if (expense is null || expense.Status != ExpenseStatus.PendingApproval) return;

                if (approval.Status == ApprovalStatus.Approved)
                {
                    expense.Status = ExpenseStatus.AccountantReview;
                    expense.ApprovedById = current.UserId;
                    expense.ApprovedAt = DateTime.UtcNow;
                }
                else
                {
                    expense.Status = ExpenseStatus.Rejected;
                    expense.RejectionReason = approval.DecisionComment;
                }
                break;
            }
        }
    }

    private static void MapNotifications(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] bool? unreadOnly, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (current.UserId is not { } userId) return Results.Unauthorized();

            var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
            if (unreadOnly == true) query = query.Where(n => !n.IsRead);

            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.Link, n.IsRead, n.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListNotifications");

        group.MapPost("/{id:guid}/read", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (current.UserId is not { } userId) return Results.Unauthorized();

            var notification = await db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
            if (notification is null) return ApiHelpers.NotFoundProblem("Notification not found.");

            notification.IsRead = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("MarkNotificationRead");

        group.MapPost("/read-all", async (AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (current.UserId is not { } userId) return Results.Unauthorized();

            await db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
            return Results.NoContent();
        })
        .WithName("MarkAllNotificationsRead");
    }

    private static WorkTaskDto ToTaskDto(WorkTask t) => new(
        t.Id, t.ReferenceNumber, t.Title, t.Description, t.Status, t.Priority,
        t.DepartmentId, t.Department?.Name ?? "", t.AssigneeId, t.Assignee?.FullName,
        t.DueDate, t.CompletedAt, t.CreatedAt,
        t.DueDate is { } due && due < DateTime.UtcNow
            && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled,
        t.RelatedEntityType, t.RelatedEntityId,
        WorkflowRules.Next(WorkflowRules.WorkTask, t.Status).Select(s => s.ToString()).ToList());

    private static ApprovalRequestDto ToApprovalDto(ApprovalRequest a, ICurrentUser current) => new(
        a.Id, a.ReferenceNumber, a.Title, a.Description, a.Status, a.EntityType, a.EntityId,
        a.Amount, a.DepartmentId, a.Department?.Name ?? "", a.RequestedBy?.FullName,
        a.DecidedBy?.FullName, a.CreatedAt, a.DecidedAt, a.DecisionComment,
        a.Status == ApprovalStatus.Pending
            && current.CanApproveDepartment(a.DepartmentId)
            && (a.RequestedById != current.UserId || current.IsSuperAdmin));
}
