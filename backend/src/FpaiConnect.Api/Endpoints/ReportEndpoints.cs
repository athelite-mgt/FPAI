using FpaiConnect.Api.Common;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Common;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace FpaiConnect.Api.Endpoints;

public static class ReportEndpoints
{
    private static readonly string[] Available =
        ["welfare-summary", "legal-summary", "finance-summary", "events-summary", "tasks-summary"];

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization();

        group.MapGet("/", () => Results.Ok(new[]
        {
            new { key = "welfare-summary", title = "Welfare Casework Summary",
                  description = "Case volumes by status, category and priority." },
            new { key = "legal-summary", title = "Legal Matters Summary",
                  description = "Matters by forum and outcome, with amounts claimed and awarded." },
            new { key = "finance-summary", title = "Finance Summary",
                  description = "Voucher and expense totals by department and status." },
            new { key = "events-summary", title = "Events & Operations Summary",
                  description = "Events by type with budget against actual spend." },
            new { key = "tasks-summary", title = "Tasks & Approvals Summary",
                  description = "Workload by status, plus approval turnaround." }
        }))
        .RequireAuthorization(Policies.ReportsRead)
        .WithName("ListReports");

        group.MapGet("/{key}", async (
            string key,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (!Available.Contains(key)) return ApiHelpers.NotFoundProblem($"Unknown report '{key}'.");

            var start = from ?? DateTime.UtcNow.AddMonths(-12);
            var end = to ?? DateTime.UtcNow;
            if (end < start) return ApiHelpers.BadRequest("The end date cannot be before the start date.");

            var report = await BuildReportAsync(key, start, end, db, current, ct);
            return Results.Ok(report);
        })
        .RequireAuthorization(Policies.ReportsRead)
        .WithName("GetReport");

        group.MapGet("/{key}/export", async (
            string key,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            if (!Available.Contains(key)) return ApiHelpers.NotFoundProblem($"Unknown report '{key}'.");

            var start = from ?? DateTime.UtcNow.AddMonths(-12);
            var end = to ?? DateTime.UtcNow;
            var report = await BuildReportAsync(key, start, end, db, current, ct);

            var csv = new StringBuilder();
            csv.AppendLine($"\"{Escape(report.Title)}\"");
            csv.AppendLine($"\"Period\",\"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}\"");
            csv.AppendLine();
            csv.AppendLine("\"Label\",\"Count\"");
            foreach (var row in report.Rows)
                csv.AppendLine($"\"{Escape(row.Label)}\",{row.Count}");
            if (report.Total is { } total)
                csv.AppendLine($"\"Total\",{total.ToString(CultureInfo.InvariantCulture)}");

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return Results.File(bytes, "text/csv", $"{key}-{DateTime.UtcNow:yyyyMMdd}.csv");
        })
        .RequireAuthorization(Policies.ReportsRead)
        .WithName("ExportReport");
    }

    private static string Escape(string value) => value.Replace("\"", "\"\"");

    private static async Task<ReportSummaryDto> BuildReportAsync(
        string key, DateTime start, DateTime end,
        AppDbContext db, ICurrentUser current, CancellationToken ct) => key switch
    {
        "welfare-summary" => await WelfareReport(start, end, db, current, ct),
        "legal-summary" => await LegalReport(start, end, db, current, ct),
        "finance-summary" => await FinanceReport(start, end, db, current, ct),
        "events-summary" => await EventsReport(start, end, db, current, ct),
        _ => await TasksReport(start, end, db, current, ct)
    };

    private static async Task<ReportSummaryDto> WelfareReport(
        DateTime start, DateTime end, AppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var scoped = db.WelfareCases.AsNoTracking().WhereReadable(current)
            .Where(c => c.OpenedAt >= start && c.OpenedAt <= end);

        var byStatus = await scoped.GroupBy(c => c.Status)
            .Select(g => new CountByLabel(g.Key.ToString(), g.Count())).ToListAsync(ct);
        var byCategory = await scoped.GroupBy(c => c.Category)
            .Select(g => new CountByLabel("Category: " + g.Key.ToString(), g.Count())).ToListAsync(ct);

        return new ReportSummaryDto("Welfare Casework Summary",
            $"Cases opened between {start:dd MMM yyyy} and {end:dd MMM yyyy}.",
            [.. byStatus, .. byCategory], await scoped.CountAsync(ct));
    }

    private static async Task<ReportSummaryDto> LegalReport(
        DateTime start, DateTime end, AppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var scoped = db.LegalCases.AsNoTracking().WhereReadable(current)
            .Where(c => c.FiledAt >= start && c.FiledAt <= end);

        var byType = await scoped.GroupBy(c => c.Type)
            .Select(g => new CountByLabel(g.Key.ToString(), g.Count())).ToListAsync(ct);
        var byOutcome = await scoped.GroupBy(c => c.Outcome)
            .Select(g => new CountByLabel("Outcome: " + g.Key.ToString(), g.Count())).ToListAsync(ct);

        return new ReportSummaryDto("Legal Matters Summary",
            $"Matters filed between {start:dd MMM yyyy} and {end:dd MMM yyyy}. " +
            $"Claimed {await scoped.SumAsync(c => c.ClaimAmount ?? 0m, ct):N0} INR, " +
            $"awarded {await scoped.SumAsync(c => c.AwardedAmount ?? 0m, ct):N0} INR.",
            [.. byType, .. byOutcome], await scoped.CountAsync(ct));
    }

    private static async Task<ReportSummaryDto> FinanceReport(
        DateTime start, DateTime end, AppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var vouchers = db.Vouchers.AsNoTracking().WhereReadable(current)
            .Where(v => v.VoucherDate >= start && v.VoucherDate <= end);
        var expenses = db.Expenses.AsNoTracking().WhereReadable(current)
            .Where(e => e.IncurredOn >= start && e.IncurredOn <= end);

        var voucherRows = await vouchers.GroupBy(v => v.Status)
            .Select(g => new CountByLabel("Vouchers: " + g.Key.ToString(), g.Count())).ToListAsync(ct);
        var expenseRows = await expenses.GroupBy(e => e.Status)
            .Select(g => new CountByLabel("Expenses: " + g.Key.ToString(), g.Count())).ToListAsync(ct);

        var voucherTotal = await vouchers.SumAsync(v => v.TotalAmount, ct);
        var expenseTotal = await expenses.SumAsync(e => e.Amount, ct);

        return new ReportSummaryDto("Finance Summary",
            $"Vouchers totalling {voucherTotal:N0} INR and expenses totalling {expenseTotal:N0} INR " +
            $"between {start:dd MMM yyyy} and {end:dd MMM yyyy}.",
            [.. voucherRows, .. expenseRows], voucherTotal + expenseTotal);
    }

    private static async Task<ReportSummaryDto> EventsReport(
        DateTime start, DateTime end, AppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var scoped = db.Events.AsNoTracking().WhereReadable(current)
            .Where(e => e.StartDate >= start && e.StartDate <= end);

        var byType = await scoped.GroupBy(e => e.Type)
            .Select(g => new CountByLabel(g.Key.ToString(), g.Count())).ToListAsync(ct);
        var byStatus = await scoped.GroupBy(e => e.Status)
            .Select(g => new CountByLabel("Status: " + g.Key.ToString(), g.Count())).ToListAsync(ct);

        return new ReportSummaryDto("Events & Operations Summary",
            $"Budget {await scoped.SumAsync(e => e.BudgetAmount, ct):N0} INR against actual " +
            $"{await scoped.SumAsync(e => e.ActualCost, ct):N0} INR, " +
            $"{await scoped.SumAsync(e => e.ActualAttendees, ct)} attendees recorded.",
            [.. byType, .. byStatus], await scoped.CountAsync(ct));
    }

    private static async Task<ReportSummaryDto> TasksReport(
        DateTime start, DateTime end, AppDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var tasks = db.WorkTasks.AsNoTracking().WhereReadable(current)
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end);
        var approvals = db.ApprovalRequests.AsNoTracking().WhereReadable(current)
            .Where(a => a.CreatedAt >= start && a.CreatedAt <= end);

        var taskRows = await tasks.GroupBy(t => t.Status)
            .Select(g => new CountByLabel("Tasks: " + g.Key.ToString(), g.Count())).ToListAsync(ct);
        var approvalRows = await approvals.GroupBy(a => a.Status)
            .Select(g => new CountByLabel("Approvals: " + g.Key.ToString(), g.Count())).ToListAsync(ct);

        var overdue = await tasks.CountAsync(t => t.DueDate != null
            && t.DueDate < DateTime.UtcNow
            && t.Status != WorkTaskStatus.Done
            && t.Status != WorkTaskStatus.Cancelled, ct);

        return new ReportSummaryDto("Tasks & Approvals Summary",
            $"{overdue} task(s) currently overdue in this period.",
            [.. taskRows, .. approvalRows], await tasks.CountAsync(ct));
    }
}
