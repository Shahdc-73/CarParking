using ParkWise.Application.Common;

namespace ParkWise.Application.Audit;

public sealed record AuditLogDto(
    Guid Id, Guid? ActorUserId, string Action, string EntityType, string EntityId,
    string? BeforeJson, string? AfterJson, DateTime TimestampUtc);

public sealed record AuditListQuery(
    string? EntityType, string? EntityId, Guid? ActorUserId, string? Action,
    DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditListQuery query, CancellationToken ct);
}
