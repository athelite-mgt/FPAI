using FpaiConnect.Api.Common;
using FpaiConnect.Api.Security;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Common;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FpaiConnect.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] string? role,
            [FromQuery] Guid? departmentId,
            [FromQuery] UserStatus? status,
            AppDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            var query = db.ActiveUsers.AsNoTracking().Include(u => u.Department).AsQueryable();

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(u => u.FullName.Contains(term)
                                         || (u.Email != null && u.Email.Contains(term)));
            }
            if (departmentId is { } d) query = query.Where(u => u.DepartmentId == d);
            if (status is { } s) query = query.Where(u => u.Status == s);

            if (!string.IsNullOrWhiteSpace(role))
            {
                // Role membership lives in the Identity join tables, so filter through them.
                var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync(ct);
                if (roleId == Guid.Empty) return Results.Ok(new PagedResult<UserListDto>([], page.Page, page.PageSize, 0));

                var userIds = db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
                query = query.Where(u => userIds.Contains(u.Id));
            }

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "email" => page.SortDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
                "lastlogin" => page.SortDescending
                    ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderBy(u => u.LastLoginAt),
                _ => page.SortDescending ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName)
            };

            var total = await query.CountAsync(ct);
            var pageItems = await query
                .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);

            // Resolve roles for the page in one round trip rather than per user.
            var ids = pageItems.Select(u => u.Id).ToList();
            var roleMap = await (from ur in db.UserRoles
                                 join r in db.Roles on ur.RoleId equals r.Id
                                 where ids.Contains(ur.UserId)
                                 select new { ur.UserId, r.Name })
                .ToListAsync(ct);

            var items = pageItems.Select(u => new UserListDto(
                u.Id, u.FullName, u.Email ?? "", u.JobTitle, u.DepartmentId, u.Department?.Name,
                roleMap.Where(x => x.UserId == u.Id).Select(x => x.Name ?? "").ToArray(),
                u.Status, u.GoogleSubjectId is not null, u.CreatedAt, u.LastLoginAt)).ToList();

            return Results.Ok(new PagedResult<UserListDto>(items, page.Page, page.PageSize, total));
        })
        .RequireAuthorization(Policies.UsersRead)
        .WithName("ListUsers");

        group.MapGet("/lookup", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.ActiveUsers.AsNoTracking()
                .Include(u => u.Department)
                .Where(u => u.Status == UserStatus.Active)
                .OrderBy(u => u.FullName)
                .Select(u => new LookupDto(u.Id, u.FullName, u.Department != null ? u.Department.Name : null))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithName("UserLookup");

        // ------------------------------------------------------------------ approval queue
        group.MapGet("/pending", async (AppDbContext db, CancellationToken ct) =>
        {
            var pending = await db.ActiveUsers.AsNoTracking()
                .Where(u => u.Status == UserStatus.PendingApproval)
                .OrderBy(u => u.CreatedAt)
                .Select(u => new PendingUserDto(u.Id, u.FullName, u.Email ?? "", u.JobTitle,
                    u.RegistrationNote, u.GoogleSubjectId != null, u.CreatedAt, u.Status.ToString()))
                .ToListAsync(ct);

            return Results.Ok(pending);
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("ListPendingUsers")
        .WithSummary("Accounts that have registered and are waiting for a decision.");

        group.MapPost("/{id:guid}/approve", async (
            Guid id, [FromBody] ApproveUserRequest request,
            UserManager<AppUser> users, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!RoleNames.All.Contains(request.Role))
                return ApiHelpers.BadRequest($"'{request.Role}' is not a valid role.");

            // Approval is where a role and department are first assigned, so the same rule as
            // user creation applies: everything except Super Admin is department-scoped.
            if (request.Role != RoleNames.SuperAdmin && request.DepartmentId is null)
                return ApiHelpers.BadRequest($"A department is required for the {request.Role} role.");
            if (request.DepartmentId is { } dept && !await db.Departments.AnyAsync(d => d.Id == dept, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");

            var user = await users.FindByIdAsync(id.ToString());
            if (user is null || user.IsDeleted) return ApiHelpers.NotFoundProblem("User not found.");
            if (user.Status is not (UserStatus.PendingApproval or UserStatus.Rejected))
                return ApiHelpers.Conflict(
                    $"This account is already {user.Status.ToString().ToLowerInvariant()}.");

            user.Status = UserStatus.Active;
            user.DepartmentId = request.DepartmentId;
            user.ApprovedById = current.UserId;
            user.ApprovedAt = DateTime.UtcNow;
            user.ApprovalNote = request.Note;
            user.EmailConfirmed = true;

            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
                return ApiHelpers.BadRequest(string.Join("; ", updated.Errors.Select(e => e.Description)));

            // A pending account has no role at all; give it exactly the one chosen here.
            var existingRoles = await users.GetRolesAsync(user);
            if (existingRoles.Count > 0) await users.RemoveFromRolesAsync(user, existingRoles);
            await users.AddToRoleAsync(user, request.Role);

            db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Title = "Your access request was approved",
                Body = $"You have been given the {request.Role} role. Welcome to FPAI Connect.",
                Link = "/",
            });
            await db.SaveChangesAsync(ct);

            var deptName = user.DepartmentId is null ? null
                : await db.Departments.Where(d => d.Id == user.DepartmentId)
                    .Select(d => d.Name).FirstOrDefaultAsync(ct);

            return Results.Ok(new UserListDto(user.Id, user.FullName, user.Email ?? "", user.JobTitle,
                user.DepartmentId, deptName, [request.Role], user.Status,
                user.GoogleSubjectId is not null, user.CreatedAt, user.LastLoginAt));
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("ApproveUser")
        .WithSummary("Approve a pending account and assign its role and department.");

        group.MapPost("/{id:guid}/reject", async (
            Guid id, [FromBody] RejectUserRequest request,
            UserManager<AppUser> users, JwtTokenService tokens, ICurrentUser current,
            CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var user = await users.FindByIdAsync(id.ToString());
            if (user is null || user.IsDeleted) return ApiHelpers.NotFoundProblem("User not found.");
            if (user.Status == UserStatus.Active)
                return ApiHelpers.Conflict(
                    "This account is already active. Suspend it instead of rejecting it.");

            user.Status = UserStatus.Rejected;
            user.ApprovedById = current.UserId;
            user.ApprovedAt = DateTime.UtcNow;
            user.ApprovalNote = request.Reason;
            await users.UpdateAsync(user);

            // Nothing should survive a rejection.
            await tokens.RevokeAllForUserAsync(user.Id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("RejectUser")
        .WithSummary("Decline a pending account. The reason is shown to the applicant.");

        group.MapGet("/roles", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.Roles.AsNoTracking()
                .Select(r => new RoleDto(r.Name ?? "", r.Description,
                    db.UserRoles.Count(ur => ur.RoleId == r.Id)))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(Policies.UsersRead)
        .WithName("ListRoles");

        group.MapPost("/", async (
            [FromBody] CreateUserRequest request,
            UserManager<AppUser> users, AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!RoleNames.All.Contains(request.Role))
                return ApiHelpers.BadRequest($"'{request.Role}' is not a valid role.");
            if (request.DepartmentId is { } dept && !await db.Departments.AnyAsync(d => d.Id == dept, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");
            if (await users.FindByEmailAsync(request.Email) is not null)
                return ApiHelpers.Conflict("A user with this email address already exists.");

            // Every role except Super Admin is scoped to a department, so one is required.
            if (request.Role != RoleNames.SuperAdmin && request.DepartmentId is null)
                return ApiHelpers.BadRequest($"A department is required for the {request.Role} role.");

            var user = new AppUser
            {
                UserName = request.Email,
                Email = request.Email,
                EmailConfirmed = true,
                FullName = request.FullName.Trim(),
                JobTitle = request.JobTitle,
                DepartmentId = request.DepartmentId,
                // With no password the account waits for its first Google sign-in.
                Status = string.IsNullOrEmpty(request.Password) ? UserStatus.Invited : UserStatus.Active
            };

            var result = string.IsNullOrEmpty(request.Password)
                ? await users.CreateAsync(user)
                : await users.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["user"] = result.Errors.Select(e => e.Description).ToArray()
                });

            await users.AddToRoleAsync(user, request.Role);

            return Results.Created($"/api/users/{user.Id}", new UserListDto(
                user.Id, user.FullName, user.Email ?? "", user.JobTitle, user.DepartmentId,
                request.DepartmentId is null ? null
                    : await db.Departments.Where(d => d.Id == request.DepartmentId)
                        .Select(d => d.Name).FirstOrDefaultAsync(ct),
                [request.Role], user.Status, false, user.CreatedAt, null));
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("CreateUser");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] UpdateUserRequest request,
            UserManager<AppUser> users, AppDbContext db, ICurrentUser current,
            JwtTokenService tokens, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!RoleNames.All.Contains(request.Role))
                return ApiHelpers.BadRequest($"'{request.Role}' is not a valid role.");

            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return ApiHelpers.NotFoundProblem("User not found.");
            if (request.Role != RoleNames.SuperAdmin && request.DepartmentId is null)
                return ApiHelpers.BadRequest($"A department is required for the {request.Role} role.");

            // Guard against locking the organisation out of its own administration.
            var currentRoles = await users.GetRolesAsync(user);
            if (currentRoles.Contains(RoleNames.SuperAdmin) && request.Role != RoleNames.SuperAdmin)
            {
                var adminCount = (await users.GetUsersInRoleAsync(RoleNames.SuperAdmin)).Count;
                if (adminCount <= 1)
                    return ApiHelpers.Conflict("The last Super Admin cannot be demoted.");
            }
            if (user.Id == current.UserId && request.Status != UserStatus.Active)
                return ApiHelpers.Conflict("You cannot suspend your own account.");

            user.FullName = request.FullName.Trim();
            user.JobTitle = request.JobTitle;
            user.DepartmentId = request.DepartmentId;
            user.Status = request.Status;
            await users.UpdateAsync(user);

            if (!currentRoles.Contains(request.Role))
            {
                await users.RemoveFromRolesAsync(user, currentRoles);
                await users.AddToRoleAsync(user, request.Role);
            }

            // Role, department and status all live in the token, so existing sessions must end.
            await tokens.RevokeAllForUserAsync(user.Id, ct);

            var deptName = user.DepartmentId is null ? null
                : await db.Departments.Where(d => d.Id == user.DepartmentId)
                    .Select(d => d.Name).FirstOrDefaultAsync(ct);

            return Results.Ok(new UserListDto(user.Id, user.FullName, user.Email ?? "", user.JobTitle,
                user.DepartmentId, deptName, [request.Role], user.Status,
                user.GoogleSubjectId is not null, user.CreatedAt, user.LastLoginAt));
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("UpdateUser");

        group.MapPost("/{id:guid}/reset-password", async (
            Guid id, [FromBody] ResetPasswordRequest request,
            UserManager<AppUser> users, JwtTokenService tokens, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return ApiHelpers.NotFoundProblem("User not found.");

            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
            if (!result.Succeeded)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = result.Errors.Select(e => e.Description).ToArray()
                });

            if (user.Status == UserStatus.Invited) user.Status = UserStatus.Active;
            await users.UpdateAsync(user);
            await tokens.RevokeAllForUserAsync(user.Id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("AdminResetPassword");

        group.MapDelete("/{id:guid}", async (
            Guid id, UserManager<AppUser> users, ICurrentUser current,
            JwtTokenService tokens, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return ApiHelpers.NotFoundProblem("User not found.");
            if (user.Id == current.UserId)
                return ApiHelpers.Conflict("You cannot delete your own account.");

            var roles = await users.GetRolesAsync(user);
            if (roles.Contains(RoleNames.SuperAdmin)
                && (await users.GetUsersInRoleAsync(RoleNames.SuperAdmin)).Count <= 1)
                return ApiHelpers.Conflict("The last Super Admin cannot be deleted.");

            // Soft delete keeps historic authorship on cases, vouchers and minutes intact.
            user.IsDeleted = true;
            user.Status = UserStatus.Suspended;
            await users.UpdateAsync(user);
            await tokens.RevokeAllForUserAsync(user.Id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("DeleteUser");

        group.MapGet("/audit", async (
            PageQuery page,
            [FromQuery] string? entityName,
            [FromQuery] Guid? userId,
            AppDbContext db, CancellationToken ct) =>
        {
            var query = db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityName)) query = query.Where(a => a.EntityName == entityName);
            if (userId is { } u) query = query.Where(a => a.UserId == u);

            var result = await query
                .OrderByDescending(a => a.Timestamp)
                .ToPagedResultAsync(page, a => new ActivityDto(
                    a.EntityName, a.Action, a.Changes ?? "", a.UserName, a.Timestamp), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("AuditTrail");
    }
}
