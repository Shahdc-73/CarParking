using System.Text.Json;
using System.Text.Json.Serialization;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;

namespace ParkWise.Application.Audit;

public sealed class AuditLogger(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Log(string action, string entityType, Guid entityId, object? before = null, object? after = null)
        => db.AuditLogs.Add(new AuditLog(
            currentUser.UserId,
            action,
            entityType,
            entityId.ToString(),
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            clock.GetUtcNow().UtcDateTime));
}
