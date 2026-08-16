using FpaiConnect.Api.Common;
using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Common;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace FpaiConnect.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (
            AppDbContext db, ICurrentUser current, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;

            var welfare = db.WelfareCases.AsNoTracking().WhereReadable(current);
            var legal = db.LegalCases.AsNoTracking().WhereReadable(current);
            var meetings = db.Meetings.AsNoTracking().WhereReadable(current);
            var tasks = db.WorkTasks.AsNoTracking().WhereReadable(current);
            var approvals = db.ApprovalRequests.AsNoTracking().WhereReadable(current);
            var ledger = db.LedgerEntries.AsNoTracking().WhereReadable(current);

            var welfareByStatus = await welfare
                .GroupBy(c => c.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var legalByType = await legal
                .Where(c => c.Status != LegalStatus.Closed)
                .GroupBy(c => c.Type)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // Six-month income vs expense.
            var trend = new List<MonthlyTrendPoint>();
            for (var back = 5; back >= 0; back--)
            {
                var point = now.AddMonths(-back);
                var monthly = await ledger
                    .Where(l => l.FiscalYear == point.Year && l.Month == point.Month)
                    .GroupBy(l => l.Direction)
                    .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
                    .ToListAsync(ct);

                trend.Add(new MonthlyTrendPoint(point.Year, point.Month,
                    CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(point.Month),
                    monthly.FirstOrDefault(x => x.Direction == LedgerDirection.Income)?.Total ?? 0m,
                    monthly.FirstOrDefault(x => x.Direction == LedgerDirection.Expense)?.Total ?? 0m));
            }

            // Voting participation over the same window.
            var participation = new List<ParticipationPoint>();
            for (var back = 4; back >= 0; back--)
            {
                var point = now.AddMonths(-back);
                // Range comparison rather than .Value.Month: date-part extraction on a nullable
                // DateTime has no SQL translation, and a range uses the index.
                var monthStart = new DateTime(point.Year, point.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);

                var closed = await db.Motions.AsNoTracking()
                    .Include(m => m.Meeting).ThenInclude(mm => mm.Attendees)
                    .Include(m => m.Votes)
                    .Where(m => m.VotingOpensAt >= monthStart && m.VotingOpensAt < monthEnd
                                && (m.Status == MotionStatus.Passed || m.Status == MotionStatus.Failed))
                    .ToListAsync(ct);

                var eligible = closed.Sum(m => m.Meeting.Attendees.Count(a => a.IsVotingMember));
                var cast = closed.Sum(m => m.Votes.Count);

                participation.Add(new ParticipationPoint(
                    CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(point.Month),
                    eligible == 0 ? 0d : Math.Round(cast / (double)eligible * 100, 1),
                    closed.Count));
            }

            var recentActivity = await db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.Timestamp)
                .Take(12)
                .Select(a => new ActivityDto(a.EntityName, a.Action,
                    $"{a.Action} {a.EntityName}", a.UserName, a.Timestamp))
                .ToListAsync(ct);

            var thisMonthExpense = await ledger
                .Where(l => l.FiscalYear == now.Year && l.Month == now.Month
                            && l.Direction == LedgerDirection.Expense)
                .SumAsync(l => l.Amount, ct);

            return Results.Ok(new DashboardDto(
                ActiveWelfareCases: welfareByStatus
                    .Where(x => x.Key < WelfareStatus.Resolved).Sum(x => x.Count),
                ActiveLegalMatters: await legal.CountAsync(c => c.Status != LegalStatus.Closed, ct),
                MonthlyExpense: thisMonthExpense,
                UpcomingMeetings: await meetings.CountAsync(
                    m => m.ScheduledAt >= now && m.Status == MeetingStatus.Scheduled, ct),
                PendingTasks: await tasks.CountAsync(
                    t => t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled, ct),
                PendingApprovals: await approvals.CountAsync(a => a.Status == ApprovalStatus.Pending, ct),
                WelfareByStatus: welfareByStatus
                    .Select(x => new CountByLabel(x.Key.ToString(), x.Count)).ToList(),
                LegalByType: legalByType
                    .Select(x => new CountByLabel(x.Key.ToString(), x.Count)).ToList(),
                FinanceTrend: trend,
                VotingTrend: participation,
                RecentActivity: recentActivity));
        })
        .RequireAuthorization()
        .WithTags("Dashboard")
        .WithName("GetDashboard");
    }
}
