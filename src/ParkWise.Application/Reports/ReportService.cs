using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Reports;

public sealed class ReportService(IAppDbContext db, TimeProvider clock) : IReportService
{
    public async Task<OccupancyReportDto> GetOccupancyAsync(CancellationToken ct)
    {
        // One grouped query for every spot of every lot: lots x statuses rows, however many spots exist.
        var rows = await (from s in db.Spots.AsNoTracking()
                          join l in db.Levels on s.LevelId equals l.Id
                          group s by new { l.LotId, s.Status } into g
                          select new { g.Key.LotId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);

        var lots = await db.Lots.AsNoTracking().OrderBy(l => l.Name).Select(l => new { l.Id, l.Name, l.IsActive }).ToListAsync(ct);

        LotOccupancyDto Build(Guid id, string name, bool active, IEnumerable<(SpotStatus Status, int Count)> counts)
        {
            var list = counts.ToList();
            int Of(SpotStatus s) => list.Where(c => c.Status == s).Sum(c => c.Count);
            var total = list.Sum(c => c.Count);
            return new LotOccupancyDto(id, name, active, total, Of(SpotStatus.Available), Of(SpotStatus.Occupied), Of(SpotStatus.Reserved),
                Of(SpotStatus.OutOfService), ReportMath.OccupancyRate(Of(SpotStatus.Occupied), total, Of(SpotStatus.OutOfService)));
        }

        var perLot = lots.Select(l => Build(l.Id, l.Name, l.IsActive, rows.Where(r => r.LotId == l.Id).Select(r => (r.Status, r.Count)))).ToList();

        // "Overall" covers OPEN lots only: a closed lot's spots are not available to anyone.
        var open = perLot.Where(l => l.IsActive).ToList();
        var overall = new LotOccupancyDto(Guid.Empty, "All open lots", true,
            open.Sum(l => l.Total), open.Sum(l => l.Available), open.Sum(l => l.Occupied), open.Sum(l => l.Reserved), open.Sum(l => l.OutOfService),
            ReportMath.OccupancyRate(open.Sum(l => l.Occupied), open.Sum(l => l.Total), open.Sum(l => l.OutOfService)));

        return new OccupancyReportDto(clock.GetUtcNow().UtcDateTime, overall, perLot);
    }

    public async Task<Result<PerformanceReportDto>> GetPerformanceAsync(PerformanceQuery query, CancellationToken ct)
    {
        var (startUtc, endUtc) = ReportMath.RangeUtc(query.From, query.To, query.UtcOffsetMinutes);

        IQueryable<Guid> spotsOfLot = query.LotId is { } lotId
            ? from s in db.Spots join l in db.Levels on s.LevelId equals l.Id where l.LotId == lotId select s.Id
            : db.Spots.Select(s => s.Id);

        if (query.LotId is { } wanted && !await db.Lots.AnyAsync(l => l.Id == wanted, ct))
            return Error.NotFound("lots.not_found", "Lot not found.");

        // Only the few columns each number needs, bounded by the 92-day limit; grouping by LOCAL day/hour is done in memory
        // because the viewer's offset is a runtime value (a database GROUP BY would have to bake it into SQL).
        var receipts = await (from r in db.Receipts.AsNoTracking()
                              join s in db.Sessions on r.SessionId equals s.Id
                              where r.IssuedAtUtc >= startUtc && r.IssuedAtUtc < endUtc && spotsOfLot.Contains(s.SpotId)
                              select new { r.IssuedAtUtc, r.Amount }).ToListAsync(ct);

        var closed = await db.Sessions.AsNoTracking()
            .Where(s => s.Status != SessionStatus.Active && s.CheckOutAtUtc >= startUtc && s.CheckOutAtUtc < endUtc && spotsOfLot.Contains(s.SpotId))
            .Select(s => new { s.Status, s.CheckInAtUtc, CheckOut = s.CheckOutAtUtc!.Value })
            .ToListAsync(ct);

        var checkIns = await db.Sessions.AsNoTracking()
            .Where(s => s.CheckInAtUtc >= startUtc && s.CheckInAtUtc < endUtc && spotsOfLot.Contains(s.SpotId))
            .Select(s => s.CheckInAtUtc).ToListAsync(ct);

        var stays = closed.Select(s => (s.CheckOut - s.CheckInAtUtc).TotalMinutes).ToList();

        return new PerformanceReportDto(
            query.From, query.To, query.LotId, query.UtcOffsetMinutes,
            TotalRevenue: receipts.Sum(r => r.Amount),
            PaidSessions: receipts.Count,
            LostTickets: closed.Count(s => s.Status == SessionStatus.LostTicket),
            CheckIns: checkIns.Count,
            AverageStayMinutes: stays.Count == 0 ? null : Math.Round(stays.Average(), 1),
            LongestStayMinutes: stays.Count == 0 ? null : Math.Round(stays.Max(), 1),
            RevenueByDay: ReportMath.RevenueByDay(query.From, query.To, receipts.Select(r => (r.IssuedAtUtc, r.Amount)), query.UtcOffsetMinutes),
            PeakHours: ReportMath.PeakHours(checkIns, query.UtcOffsetMinutes));
    }
}
