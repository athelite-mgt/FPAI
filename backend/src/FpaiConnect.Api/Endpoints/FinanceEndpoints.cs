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
using System.Globalization;

namespace FpaiConnect.Api.Endpoints;

public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        MapVouchers(app);
        MapExpenses(app);
        MapInvoices(app);
        MapQueries(app);
        MapSummary(app);
    }

    // ------------------------------------------------------------------ vouchers
    private static void MapVouchers(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/vouchers").WithTags("Finance").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] VoucherStatus? status,
            [FromQuery] Guid? departmentId,
            [FromQuery] Guid? vendorId,
            [FromQuery] int? year,
            [FromQuery] int? month,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.Vouchers.AsNoTracking()
                .Include(v => v.Vendor).Include(v => v.Department)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(v => v.VoucherNumber.Contains(term) || v.Vendor.Name.Contains(term));
            }
            if (status is { } s) query = query.Where(v => v.Status == s);
            if (departmentId is { } d) query = query.Where(v => v.DepartmentId == d);
            if (vendorId is { } ven) query = query.Where(v => v.VendorId == ven);

            // Date filtering by range: date-part extraction on DateTime does not
            // translate reliably across providers, and a range can use the index.
            if (year is { } y)
            {
                if (month is { } ym)
                {
                    var from = new DateTime(y, Math.Clamp(ym, 1, 12), 1, 0, 0, 0, DateTimeKind.Utc);
                    query = query.Where(v => v.VoucherDate >= from && v.VoucherDate < from.AddMonths(1));
                }
                else
                {
                    var from = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    query = query.Where(v => v.VoucherDate >= from && v.VoucherDate < from.AddYears(1));
                }
            }
            else if (month is { } monthOnly)
            {
                // Month without a year means "that month of the current year".
                var from = new DateTime(DateTime.UtcNow.Year,
                    Math.Clamp(monthOnly, 1, 12), 1, 0, 0, 0, DateTimeKind.Utc);
                query = query.Where(v => v.VoucherDate >= from && v.VoucherDate < from.AddMonths(1));
            }

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "amount" => page.SortDescending
                    ? query.OrderByDescending(v => v.TotalAmount) : query.OrderBy(v => v.TotalAmount),
                "vendor" => page.SortDescending
                    ? query.OrderByDescending(v => v.Vendor.Name) : query.OrderBy(v => v.Vendor.Name),
                "status" => page.SortDescending
                    ? query.OrderByDescending(v => v.Status) : query.OrderBy(v => v.Status),
                _ => page.SortDescending
                    ? query.OrderByDescending(v => v.VoucherDate) : query.OrderBy(v => v.VoucherDate)
            };

            var result = await query
                .Select(v => new VoucherListDto(v.Id, v.VoucherNumber, v.VendorId, v.Vendor.Name,
                    v.DepartmentId, v.Department.Name, v.Amount, v.TaxAmount, v.TotalAmount,
                    v.Currency, v.Status, v.VoucherDate,
                    v.Queries.Count(q => q.Status == QueryStatus.Open)))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("ListVouchers");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var v = await LoadVoucher(db, id, ct);
            if (v is null) return ApiHelpers.NotFoundProblem("Voucher not found.");
            if (!current.CanReadDepartment(v.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this voucher.");
            return Results.Ok(ToVoucherDetail(v));
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("GetVoucher");

        group.MapPost("/", async (
            [FromBody] VoucherUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!current.CanWriteDepartment(request.DepartmentId))
                return ApiHelpers.Forbidden("You cannot raise vouchers for this department.");
            if (!await db.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");
            if (!await db.Vendors.AnyAsync(v => v.Id == request.VendorId, ct))
                return ApiHelpers.BadRequest("The selected vendor does not exist.");

            var voucher = new Voucher
            {
                VoucherNumber = await refs.NextVoucherAsync(ct),
                DepartmentId = request.DepartmentId,
                VendorId = request.VendorId,
                Amount = request.Amount,
                TaxAmount = request.TaxAmount,
                TotalAmount = request.Amount + request.TaxAmount,
                Status = VoucherStatus.Draft,
                VoucherDate = request.VoucherDate ?? DateTime.UtcNow,
                Description = request.Description
            };
            db.Vouchers.Add(voucher);
            await db.SaveChangesAsync(ct);

            var created = await LoadVoucher(db, voucher.Id, ct);
            return Results.Created($"/api/finance/vouchers/{voucher.Id}", ToVoucherDetail(created!));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("CreateVoucher");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] VoucherUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (voucher is null) return ApiHelpers.NotFoundProblem("Voucher not found.");
            if (!current.CanWriteDepartment(voucher.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this voucher.");
            // Once money is committed the figures are frozen; corrections go through rejection.
            if (voucher.Status is not (VoucherStatus.Draft or VoucherStatus.Rejected))
                return ApiHelpers.Conflict($"A voucher in {voucher.Status} status can no longer be edited.");

            voucher.VendorId = request.VendorId;
            voucher.Amount = request.Amount;
            voucher.TaxAmount = request.TaxAmount;
            voucher.TotalAmount = request.Amount + request.TaxAmount;
            voucher.VoucherDate = request.VoucherDate ?? voucher.VoucherDate;
            voucher.Description = request.Description;
            await db.SaveChangesAsync(ct);

            var updated = await LoadVoucher(db, id, ct);
            return Results.Ok(ToVoucherDetail(updated!));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("UpdateVoucher");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<VoucherStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid voucher status.");

            var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (voucher is null) return ApiHelpers.NotFoundProblem("Voucher not found.");

            // Each transition has its own authority requirement.
            var authorised = target switch
            {
                VoucherStatus.Approved or VoucherStatus.Rejected => current.CanApproveDepartment(voucher.DepartmentId),
                VoucherStatus.Reconciled or VoucherStatus.Closed =>
                    current.IsSuperAdmin || current.IsInRole(RoleNames.ExternalAccountant),
                _ => current.CanWriteDepartment(voucher.DepartmentId)
            };
            if (!authorised)
                return ApiHelpers.Forbidden(target switch
                {
                    VoucherStatus.Approved or VoucherStatus.Rejected =>
                        "Only the department head or an administrator can approve or reject a voucher.",
                    VoucherStatus.Reconciled or VoucherStatus.Closed =>
                        "Only the external accountant or an administrator can reconcile a voucher.",
                    _ => "You cannot change this voucher."
                });

            if (!WorkflowRules.CanTransition(WorkflowRules.Voucher, voucher.Status, target))
                return ApiHelpers.Conflict(
                    $"A voucher cannot move from {voucher.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.Voucher, voucher.Status))}.");

            if (target == VoucherStatus.Rejected && string.IsNullOrWhiteSpace(request.Comment))
                return ApiHelpers.BadRequest("A reason is required when rejecting a voucher.");

            // An open accountant query blocks reconciliation.
            if (target == VoucherStatus.Reconciled
                && await db.AccountantQueries.AnyAsync(q => q.VoucherId == id && q.Status == QueryStatus.Open, ct))
                return ApiHelpers.Conflict("Resolve the open accountant queries before reconciling.");

            voucher.Status = target;
            switch (target)
            {
                case VoucherStatus.Approved:
                    voucher.ApprovedById = current.UserId;
                    voucher.ApprovedAt = DateTime.UtcNow;
                    voucher.RejectionReason = null;
                    break;
                case VoucherStatus.Rejected:
                    voucher.RejectionReason = request.Comment;
                    voucher.ApprovedById = null;
                    voucher.ApprovedAt = null;
                    break;
                case VoucherStatus.Reconciled:
                    voucher.ReconciledById = current.UserId;
                    voucher.ReconciledAt = DateTime.UtcNow;
                    break;
            }
            await db.SaveChangesAsync(ct);

            var updated = await LoadVoucher(db, id, ct);
            return Results.Ok(ToVoucherDetail(updated!));
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("TransitionVoucher");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (voucher is null) return ApiHelpers.NotFoundProblem("Voucher not found.");
            if (!current.CanApproveDepartment(voucher.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can delete a voucher.");
            if (voucher.Status >= VoucherStatus.Approved && voucher.Status != VoucherStatus.Rejected)
                return ApiHelpers.Conflict("An approved voucher cannot be deleted.");

            db.Vouchers.Remove(voucher);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceApprove)
        .WithName("DeleteVoucher");
    }

    // ------------------------------------------------------------------ expenses
    private static void MapExpenses(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/expenses").WithTags("Finance").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] ExpenseStatus? status,
            [FromQuery] Guid? departmentId,
            [FromQuery] string? category,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var query = db.Expenses.AsNoTracking()
                .Include(e => e.Department).Include(e => e.SubmittedBy)
                .WhereReadable(current);

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(e => e.ExpenseNumber.Contains(term) || e.Title.Contains(term));
            }
            if (status is { } s) query = query.Where(e => e.Status == s);
            if (departmentId is { } d) query = query.Where(e => e.DepartmentId == d);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(e => e.Category == category);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "amount" => page.SortDescending
                    ? query.OrderByDescending(e => e.Amount) : query.OrderBy(e => e.Amount),
                "status" => page.SortDescending
                    ? query.OrderByDescending(e => e.Status) : query.OrderBy(e => e.Status),
                _ => page.SortDescending
                    ? query.OrderByDescending(e => e.IncurredOn) : query.OrderBy(e => e.IncurredOn)
            };

            var result = await query
                .Select(e => new ExpenseListDto(e.Id, e.ExpenseNumber, e.Title, e.Category,
                    e.DepartmentId, e.Department.Name, e.Amount, e.Currency, e.Status, e.IncurredOn,
                    e.SubmittedBy != null ? e.SubmittedBy.FullName : null, e.Invoices.Count))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("ListExpenses");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var e = await LoadExpense(db, id, ct);
            if (e is null) return ApiHelpers.NotFoundProblem("Expense not found.");
            if (!current.CanReadDepartment(e.DepartmentId))
                return ApiHelpers.Forbidden("You do not have access to this expense.");
            return Results.Ok(ToExpenseDetail(e));
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("GetExpense");

        group.MapPost("/", async (
            [FromBody] ExpenseUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!current.CanWriteDepartment(request.DepartmentId))
                return ApiHelpers.Forbidden("You cannot raise expenses for this department.");
            if (!await db.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct))
                return ApiHelpers.BadRequest("The selected department does not exist.");

            var expense = new Expense
            {
                ExpenseNumber = await refs.NextExpenseAsync(ct),
                DepartmentId = request.DepartmentId,
                Title = request.Title.Trim(),
                Category = request.Category,
                Amount = request.Amount,
                Status = ExpenseStatus.Created,
                IncurredOn = request.IncurredOn ?? DateTime.UtcNow,
                Description = request.Description,
                SubmittedById = current.UserId
            };
            db.Expenses.Add(expense);
            await db.SaveChangesAsync(ct);

            var created = await LoadExpense(db, expense.Id, ct);
            return Results.Created($"/api/finance/expenses/{expense.Id}", ToExpenseDetail(created!));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("CreateExpense");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] ExpenseUpsertRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null) return ApiHelpers.NotFoundProblem("Expense not found.");
            if (!current.CanWriteDepartment(expense.DepartmentId))
                return ApiHelpers.Forbidden("You cannot modify this expense.");
            if (expense.Status is not (ExpenseStatus.Created or ExpenseStatus.InvoiceAttached or ExpenseStatus.Rejected))
                return ApiHelpers.Conflict($"An expense in {expense.Status} status can no longer be edited.");

            expense.Title = request.Title.Trim();
            expense.Category = request.Category;
            expense.Amount = request.Amount;
            expense.IncurredOn = request.IncurredOn ?? expense.IncurredOn;
            expense.Description = request.Description;
            await db.SaveChangesAsync(ct);

            var updated = await LoadExpense(db, id, ct);
            return Results.Ok(ToExpenseDetail(updated!));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("UpdateExpense");

        group.MapPost("/{id:guid}/status", async (
            Guid id, [FromBody] StatusTransitionRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (!Enum.TryParse<ExpenseStatus>(request.Status, ignoreCase: true, out var target))
                return ApiHelpers.BadRequest($"'{request.Status}' is not a valid expense status.");

            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null) return ApiHelpers.NotFoundProblem("Expense not found.");

            var authorised = target switch
            {
                ExpenseStatus.AccountantReview or ExpenseStatus.Rejected =>
                    current.CanApproveDepartment(expense.DepartmentId),
                ExpenseStatus.Reconciled or ExpenseStatus.Closed =>
                    current.IsSuperAdmin || current.IsInRole(RoleNames.ExternalAccountant),
                _ => current.CanWriteDepartment(expense.DepartmentId)
            };
            if (!authorised)
                return ApiHelpers.Forbidden("You are not authorised to make this transition.");

            if (!WorkflowRules.CanTransition(WorkflowRules.Expense, expense.Status, target))
                return ApiHelpers.Conflict(
                    $"An expense cannot move from {expense.Status} to {target}. " +
                    $"Allowed: {string.Join(", ", WorkflowRules.Next(WorkflowRules.Expense, expense.Status))}.");

            if (target == ExpenseStatus.Rejected && string.IsNullOrWhiteSpace(request.Comment))
                return ApiHelpers.BadRequest("A reason is required when rejecting an expense.");

            if (target == ExpenseStatus.Reconciled
                && await db.AccountantQueries.AnyAsync(q => q.ExpenseId == id && q.Status == QueryStatus.Open, ct))
                return ApiHelpers.Conflict("Resolve the open accountant queries before reconciling.");

            expense.Status = target;
            switch (target)
            {
                case ExpenseStatus.AccountantReview:
                    expense.ApprovedById = current.UserId;
                    expense.ApprovedAt = DateTime.UtcNow;
                    expense.RejectionReason = null;
                    break;
                case ExpenseStatus.Rejected:
                    expense.RejectionReason = request.Comment;
                    expense.ApprovedById = null;
                    expense.ApprovedAt = null;
                    break;
            }
            await db.SaveChangesAsync(ct);

            var updated = await LoadExpense(db, id, ct);
            return Results.Ok(ToExpenseDetail(updated!));
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("TransitionExpense");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null) return ApiHelpers.NotFoundProblem("Expense not found.");
            if (!current.CanApproveDepartment(expense.DepartmentId))
                return ApiHelpers.Forbidden("Only a department head or administrator can delete an expense.");
            if (expense.Status >= ExpenseStatus.AccountantReview && expense.Status != ExpenseStatus.Rejected)
                return ApiHelpers.Conflict("An expense under review or reconciled cannot be deleted.");

            db.Expenses.Remove(expense);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceApprove)
        .WithName("DeleteExpense");
    }

    // ------------------------------------------------------------------ invoices
    private static void MapInvoices(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/invoices").WithTags("Finance").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] InvoiceStatus? status,
            [FromQuery] Guid? expenseId,
            AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Invoices.AsNoTracking().Include(i => i.Vendor).AsQueryable();

            if (!string.IsNullOrWhiteSpace(page.Search))
                query = query.Where(i => i.InvoiceNumber.Contains(page.Search.Trim()));
            if (status is { } s) query = query.Where(i => i.Status == s);
            if (expenseId is { } e) query = query.Where(i => i.ExpenseId == e);

            var result = await query
                .OrderByDescending(i => i.IssuedOn)
                .Select(i => new InvoiceDto(i.Id, i.InvoiceNumber, i.VendorId,
                    i.Vendor != null ? i.Vendor.Name : null, i.ExpenseId, i.Amount, i.TaxAmount,
                    i.Currency, i.Status, i.IssuedOn, i.DueDate, i.PaidOn))
                .ToPagedResultAsync(page, x => x, ct);

            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("ListInvoices");

        group.MapPost("/", async (
            [FromBody] InvoiceUpsertRequest request,
            AppDbContext db, ICurrentUser current, ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            Expense? expense = null;
            if (request.ExpenseId is { } eid)
            {
                expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == eid, ct);
                if (expense is null) return ApiHelpers.BadRequest("The selected expense does not exist.");
                if (!current.CanWriteDepartment(expense.DepartmentId))
                    return ApiHelpers.Forbidden("You cannot attach invoices to this expense.");
            }
            if (request.VendorId is { } vid && !await db.Vendors.AnyAsync(v => v.Id == vid, ct))
                return ApiHelpers.BadRequest("The selected vendor does not exist.");

            var invoice = new Invoice
            {
                InvoiceNumber = await refs.NextInvoiceAsync(ct),
                VendorId = request.VendorId,
                ExpenseId = request.ExpenseId,
                Amount = request.Amount,
                TaxAmount = request.TaxAmount,
                Status = request.Status,
                IssuedOn = request.IssuedOn ?? DateTime.UtcNow,
                DueDate = request.DueDate
            };
            db.Invoices.Add(invoice);

            // Attaching the first invoice advances the expense workflow automatically.
            if (expense is { Status: ExpenseStatus.Created }) expense.Status = ExpenseStatus.InvoiceAttached;

            await db.SaveChangesAsync(ct);

            var vendorName = invoice.VendorId is null ? null
                : await db.Vendors.Where(v => v.Id == invoice.VendorId).Select(v => v.Name).FirstOrDefaultAsync(ct);

            return Results.Created($"/api/finance/invoices/{invoice.Id}", new InvoiceDto(
                invoice.Id, invoice.InvoiceNumber, invoice.VendorId, vendorName, invoice.ExpenseId,
                invoice.Amount, invoice.TaxAmount, invoice.Currency, invoice.Status,
                invoice.IssuedOn, invoice.DueDate, invoice.PaidOn));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("CreateInvoice");

        group.MapPost("/{id:guid}/pay", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return ApiHelpers.NotFoundProblem("Invoice not found.");
            if (invoice.Status == InvoiceStatus.Paid)
                return ApiHelpers.Conflict("This invoice is already marked paid.");

            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidOn = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceApprove)
        .WithName("MarkInvoicePaid");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return ApiHelpers.NotFoundProblem("Invoice not found.");
            if (invoice.Status == InvoiceStatus.Paid)
                return ApiHelpers.Conflict("A paid invoice cannot be deleted.");

            db.Invoices.Remove(invoice);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceApprove)
        .WithName("DeleteInvoice");
    }

    // ------------------------------------------------------------------ accountant queries
    private static void MapQueries(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/queries").WithTags("Finance").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] QueryStatus? status,
            AppDbContext db, CancellationToken ct) =>
        {
            var query = db.AccountantQueries.AsNoTracking()
                .Include(q => q.Voucher).Include(q => q.Expense)
                .Include(q => q.RaisedBy).Include(q => q.AnsweredBy)
                .AsQueryable();

            if (status is { } s) query = query.Where(q => q.Status == s);

            var result = await query
                .OrderByDescending(q => q.CreatedAt)
                .ToPagedResultAsync(page, ToQueryDto, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("ListAccountantQueries");

        group.MapPost("/", async (
            [FromBody] RaiseQueryRequest request, AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (request.VoucherId is null && request.ExpenseId is null)
                return ApiHelpers.BadRequest("A query must reference either a voucher or an expense.");
            if (request.VoucherId is not null && request.ExpenseId is not null)
                return ApiHelpers.BadRequest("A query can reference only one record.");

            if (request.VoucherId is { } vid && !await db.Vouchers.AnyAsync(v => v.Id == vid, ct))
                return ApiHelpers.BadRequest("The referenced voucher does not exist.");
            if (request.ExpenseId is { } eid && !await db.Expenses.AnyAsync(e => e.Id == eid, ct))
                return ApiHelpers.BadRequest("The referenced expense does not exist.");

            var query = new AccountantQuery
            {
                VoucherId = request.VoucherId,
                ExpenseId = request.ExpenseId,
                Question = request.Question.Trim(),
                Status = QueryStatus.Open,
                RaisedById = current.UserId
            };
            db.AccountantQueries.Add(query);
            await db.SaveChangesAsync(ct);

            var saved = await db.AccountantQueries.AsNoTracking()
                .Include(q => q.Voucher).Include(q => q.Expense)
                .Include(q => q.RaisedBy).Include(q => q.AnsweredBy)
                .FirstAsync(q => q.Id == query.Id, ct);
            return Results.Created($"/api/finance/queries/{query.Id}", ToQueryDto(saved));
        })
        .RequireAuthorization(Policies.FinanceReconcile)
        .WithName("RaiseAccountantQuery");

        group.MapPost("/{id:guid}/answer", async (
            Guid id, [FromBody] AnswerQueryRequest request,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var query = await db.AccountantQueries.FirstOrDefaultAsync(q => q.Id == id, ct);
            if (query is null) return ApiHelpers.NotFoundProblem("Query not found.");
            if (query.Status == QueryStatus.Resolved)
                return ApiHelpers.Conflict("This query is already resolved.");

            query.Response = request.Response.Trim();
            query.Status = QueryStatus.Answered;
            query.AnsweredById = current.UserId;
            query.AnsweredAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("AnswerAccountantQuery");

        group.MapPost("/{id:guid}/resolve", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var query = await db.AccountantQueries.FirstOrDefaultAsync(q => q.Id == id, ct);
            if (query is null) return ApiHelpers.NotFoundProblem("Query not found.");

            query.Status = QueryStatus.Resolved;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceReconcile)
        .WithName("ResolveAccountantQuery");
    }

    // ------------------------------------------------------------------ summary
    private static void MapSummary(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/finance/summary", async (
            [FromQuery] int? months,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var window = Math.Clamp(months ?? 6, 1, 24);
            var now = DateTime.UtcNow;
            var scopedLedger = db.LedgerEntries.AsNoTracking().WhereReadable(current);

            var trend = new List<MonthlyTrendPoint>();
            for (var back = window - 1; back >= 0; back--)
            {
                var point = now.AddMonths(-back);
                var income = await scopedLedger
                    .Where(l => l.FiscalYear == point.Year && l.Month == point.Month
                                && l.Direction == LedgerDirection.Income)
                    .SumAsync(l => l.Amount, ct);
                var expense = await scopedLedger
                    .Where(l => l.FiscalYear == point.Year && l.Month == point.Month
                                && l.Direction == LedgerDirection.Expense)
                    .SumAsync(l => l.Amount, ct);

                trend.Add(new MonthlyTrendPoint(point.Year, point.Month,
                    CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(point.Month),
                    income, expense));
            }

            var byDepartment = await scopedLedger
                .Where(l => l.FiscalYear == now.Year && l.Month == now.Month
                            && l.Direction == LedgerDirection.Expense)
                .Include(l => l.Department)
                .GroupBy(l => new { l.DepartmentId, l.Department.Name })
                .Select(g => new DepartmentSpendDto(g.Key.DepartmentId, g.Key.Name,
                    g.Sum(x => x.Amount), g.Sum(x => x.BudgetedAmount)))
                .ToListAsync(ct);

            var current_ = trend.LastOrDefault();
            return Results.Ok(new FinanceSummaryDto(
                current_?.Income ?? 0m,
                current_?.Expense ?? 0m,
                await db.Vouchers.WhereReadable(current).CountAsync(v => v.Status == VoucherStatus.Pending, ct),
                await db.AccountantQueries.CountAsync(q => q.Status == QueryStatus.Open, ct),
                trend, byDepartment));
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithTags("Finance")
        .WithName("FinanceSummary");
    }

    // ------------------------------------------------------------------ helpers
    private static Task<Voucher?> LoadVoucher(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Vouchers.AsNoTracking()
            .Include(v => v.Vendor).Include(v => v.Department)
            .Include(v => v.ApprovedBy).Include(v => v.ReconciledBy)
            .Include(v => v.Queries).ThenInclude(q => q.RaisedBy)
            .Include(v => v.Queries).ThenInclude(q => q.AnsweredBy)
            .Include(v => v.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    private static VoucherDetailDto ToVoucherDetail(Voucher v) => new(
        v.Id, v.VoucherNumber, v.VendorId, v.Vendor.Name, v.DepartmentId, v.Department.Name,
        v.Amount, v.TaxAmount, v.TotalAmount, v.Currency, v.Status, v.VoucherDate, v.Description,
        v.ApprovedBy?.FullName, v.ApprovedAt, v.RejectionReason,
        v.ReconciledBy?.FullName, v.ReconciledAt,
        v.Queries.Select(ToQueryDto).ToList(),
        v.Documents.Select(DocumentEndpoints.ToListDto).ToList(),
        WorkflowRules.Next(WorkflowRules.Voucher, v.Status).Select(s => s.ToString()).ToList());

    private static Task<Expense?> LoadExpense(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Expenses.AsNoTracking()
            .Include(e => e.Department).Include(e => e.SubmittedBy).Include(e => e.ApprovedBy)
            .Include(e => e.Invoices).ThenInclude(i => i.Vendor)
            .Include(e => e.Queries).ThenInclude(q => q.RaisedBy)
            .Include(e => e.Queries).ThenInclude(q => q.AnsweredBy)
            .Include(e => e.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    private static ExpenseDetailDto ToExpenseDetail(Expense e) => new(
        e.Id, e.ExpenseNumber, e.Title, e.Category, e.Description,
        e.DepartmentId, e.Department.Name, e.Amount, e.Currency, e.Status, e.IncurredOn,
        e.SubmittedBy?.FullName, e.ApprovedBy?.FullName, e.ApprovedAt, e.RejectionReason,
        e.Invoices.Select(i => new InvoiceDto(i.Id, i.InvoiceNumber, i.VendorId, i.Vendor?.Name,
            i.ExpenseId, i.Amount, i.TaxAmount, i.Currency, i.Status, i.IssuedOn, i.DueDate, i.PaidOn)).ToList(),
        e.Queries.Select(ToQueryDto).ToList(),
        e.Documents.Select(DocumentEndpoints.ToListDto).ToList(),
        WorkflowRules.Next(WorkflowRules.Expense, e.Status).Select(s => s.ToString()).ToList());

    private static AccountantQueryDto ToQueryDto(AccountantQuery q) => new(
        q.Id, q.VoucherId, q.Voucher?.VoucherNumber, q.ExpenseId, q.Expense?.ExpenseNumber,
        q.Question, q.Response, q.Status, q.RaisedBy?.FullName, q.AnsweredBy?.FullName,
        q.CreatedAt, q.AnsweredAt);
}
