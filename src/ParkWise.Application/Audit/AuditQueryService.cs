using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;

namespace ParkWise.Application.Audit;

public sealed class AuditQueryService(IAppDbContext db) : IAuditQueryService
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditListQuery query, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) q = q.Where(a => a.EntityId == query.EntityId);
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action == query.Action);
        if (query.ActorUserId is { } actor) q = q.Where(a => a.ActorUserId == actor);
        if (query.FromUtc is { } from) q = q.Where(a => a.TimestampUtc >= from);
        if (query.ToUtc is { } to) q = q.Where(a => a.TimestampUtc < to);

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.TimestampUtc)
            .Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(a => new AuditLogDto(a.Id, a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.BeforeJson, a.AfterJson, a.TimestampUtc))
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, paging.SafePage, paging.SafePageSize, total);
    }
}
