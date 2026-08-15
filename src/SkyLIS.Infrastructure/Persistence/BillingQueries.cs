using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Billing;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class BillingQueries : IBillingQueries
{
    private readonly SkyLisDbContext _db;
    public BillingQueries(SkyLisDbContext db) => _db = db;

    public async Task<InvoiceDetailsDto?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new
            {
                i.Id, i.InvoiceNumber, i.VisitId, i.BranchId, i.Status,
                Total = i.Total.Amount, i.Total.Currency,
                i.DiscountAmount, i.DiscountReason, i.CreditedAmount,
                Payments = i.Payments.OrderBy(p => p.CapturedAtUtc).Select(p => new
                {
                    p.Id, p.Amount.Amount, p.Amount.Currency, p.Method, p.IsRefund, p.Reason, p.CapturedAtUtc,
                }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (invoice is null) return null;

        var visitNumber = await _db.Visits.AsNoTracking()
            .Where(v => v.Id == invoice.VisitId).Select(v => v.VisitNumber).FirstOrDefaultAsync(ct) ?? "?";
        var branchCode = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == invoice.BranchId).Select(b => b.Code).FirstOrDefaultAsync(ct) ?? "?";
        var creditNotes = await _db.CreditNotes.AsNoTracking()
            .Where(c => c.InvoiceId == invoice.Id)
            .OrderBy(c => c.IssuedAtUtc)
            .Select(c => new CreditNoteDto(
                c.Id, c.CreditNoteNumber, c.Amount.Amount, c.Amount.Currency, c.Reason, c.IssuedAtUtc))
            .ToListAsync(ct);

        var paid = invoice.Payments.Where(p => !p.IsRefund).Sum(p => p.Amount);
        var refunded = invoice.Payments.Where(p => p.IsRefund).Sum(p => p.Amount);
        var balance = invoice.Total - invoice.DiscountAmount - invoice.CreditedAmount - (paid - refunded);

        return new InvoiceDetailsDto(
            invoice.Id, invoice.InvoiceNumber, invoice.VisitId, visitNumber, branchCode,
            invoice.Status.ToString(), invoice.Total, invoice.DiscountAmount, invoice.DiscountReason,
            invoice.CreditedAmount, paid, refunded, balance, invoice.Currency,
            invoice.Payments.Select(p => new InvoicePaymentDto(
                p.Id, p.Amount, p.Currency, p.Method, p.IsRefund, p.Reason, p.CapturedAtUtc)).ToList(),
            creditNotes);
    }

    public async Task<IReadOnlyList<MethodTotalDto>> MethodTotalsAsync(
        Guid branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => i.BranchId == branchId)
            .SelectMany(i => i.Payments)
            .Where(p => p.CapturedAtUtc >= fromUtc && p.CapturedAtUtc < toUtc)
            .GroupBy(p => new { p.Method, p.IsRefund })
            .Select(g => new { g.Key.Method, g.Key.IsRefund, Sum = g.Sum(p => p.Amount.Amount) })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Method)
            .Select(g => new MethodTotalDto(
                g.Key,
                g.Where(r => !r.IsRefund).Sum(r => r.Sum),
                g.Where(r => r.IsRefund).Sum(r => r.Sum)))
            .OrderBy(m => m.Method)
            .ToList();
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default)
    {
        var shifts = await _db.CashierShifts.AsNoTracking()
            .OrderByDescending(s => s.OpenedAtUtc)
            .Take(50)
            .Select(s => new
            {
                s.Id, s.BranchId, s.Status,
                OpeningFloat = s.OpeningFloat.Amount, s.OpeningFloat.Currency,
                s.OpenedAtUtc, s.ClosedAtUtc, s.DeclaredCash, s.ExpectedCash, s.Variance,
            })
            .ToListAsync(ct);
        var branchCodes = await _db.Branches.AsNoTracking()
            .Select(b => new { b.Id, b.Code }).ToListAsync(ct);
        var codeById = branchCodes.ToDictionary(b => b.Id, b => b.Code);

        return shifts.Select(s => new ShiftDto(
            s.Id, s.BranchId, codeById.GetValueOrDefault(s.BranchId, "?"), s.Status.ToString(),
            s.OpeningFloat, s.Currency, s.OpenedAtUtc, s.ClosedAtUtc,
            s.DeclaredCash, s.ExpectedCash, s.Variance)).ToList();
    }
}
