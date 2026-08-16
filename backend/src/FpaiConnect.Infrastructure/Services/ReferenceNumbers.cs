using FpaiConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FpaiConnect.Infrastructure.Services;

/// <summary>
/// Allocates the human-readable reference numbers users quote to each other
/// (WEL/2025/101, V-2240, MTG-2025-021 ...). Sequences restart per prefix.
/// </summary>
public class ReferenceNumberGenerator(AppDbContext db)
{
    public async Task<string> NextWelfareCaseAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"WEL/{year}/";
        var next = await NextSequenceAsync(
            db.WelfareCases.IgnoreQueryFilters().Select(x => x.CaseNumber), prefix, 101, ct);
        return $"{prefix}{next:D3}";
    }

    public async Task<string> NextLegalCaseAsync(string typePrefix, CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"{typePrefix}/{year}/";
        var next = await NextSequenceAsync(
            db.LegalCases.IgnoreQueryFilters().Select(x => x.CaseNumber), prefix, 1, ct);
        return $"{prefix}{next:D3}";
    }

    public async Task<string> NextVoucherAsync(CancellationToken ct = default)
    {
        var next = await NextSequenceAsync(
            db.Vouchers.IgnoreQueryFilters().Select(x => x.VoucherNumber), "V-", 2200, ct);
        return $"V-{next}";
    }

    public async Task<string> NextExpenseAsync(CancellationToken ct = default)
    {
        var next = await NextSequenceAsync(
            db.Expenses.IgnoreQueryFilters().Select(x => x.ExpenseNumber), "EXP-", 5100, ct);
        return $"EXP-{next}";
    }

    public async Task<string> NextInvoiceAsync(CancellationToken ct = default)
    {
        var next = await NextSequenceAsync(
            db.Invoices.IgnoreQueryFilters().Select(x => x.InvoiceNumber), "INV-", 9000, ct);
        return $"INV-{next}";
    }

    public async Task<string> NextMeetingAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"MTG-{year}-";
        var next = await NextSequenceAsync(
            db.Meetings.IgnoreQueryFilters().Select(x => x.ReferenceNumber), prefix, 1, ct);
        return $"{prefix}{next:D3}";
    }

    public async Task<string> NextEventAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"EVT-{year}-";
        var next = await NextSequenceAsync(
            db.Events.IgnoreQueryFilters().Select(x => x.ReferenceNumber), prefix, 1, ct);
        return $"{prefix}{next:D3}";
    }

    public async Task<string> NextTaskAsync(CancellationToken ct = default)
    {
        var next = await NextSequenceAsync(
            db.WorkTasks.IgnoreQueryFilters().Select(x => x.ReferenceNumber), "TSK-", 4100, ct);
        return $"TSK-{next}";
    }

    public async Task<string> NextApprovalAsync(CancellationToken ct = default)
    {
        var next = await NextSequenceAsync(
            db.ApprovalRequests.IgnoreQueryFilters().Select(x => x.ReferenceNumber), "APR-", 7100, ct);
        return $"APR-{next}";
    }

    public async Task<string> NextPlayerMembershipAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"FPAI-{year}-";
        var next = await NextSequenceAsync(
            db.Players.IgnoreQueryFilters().Select(x => x.MembershipId), prefix, 1, ct);
        return $"{prefix}{next:D4}";
    }

    /// <summary>
    /// Finds the highest numeric suffix already issued for a prefix and returns the next one.
    /// The unique index on each reference column is the real guard against duplicates.
    /// </summary>
    private static async Task<int> NextSequenceAsync(
        IQueryable<string> column, string prefix, int seed, CancellationToken ct)
    {
        var existing = await column
            .Where(v => v.StartsWith(prefix))
            .ToListAsync(ct);

        var highest = existing
            .Select(v => v[prefix.Length..])
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return highest == 0 ? seed : highest + 1;
    }
}
