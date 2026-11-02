using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;
using ParkWise.Domain.Services;

namespace ParkWise.Domain.Entities;

/// <summary>
/// Pricing is versioned: an admin "change" inserts a new row with a later EffectiveFromUtc.
/// The rule in force at check-out time is the newest one whose EffectiveFromUtc has passed.
/// </summary>
public class PricingRule : Entity
{
    private PricingRule() { }

    public PricingRule(SpotType spotType, decimal hourlyRate, decimal dailyCap, int freeMinutes, DateTime effectiveFromUtc)
    {
        if (hourlyRate < 0) throw new DomainException("Hourly rate cannot be negative.");
        if (dailyCap < hourlyRate) throw new DomainException("Daily cap cannot be lower than the hourly rate.");
        if (freeMinutes < 0) throw new DomainException("Free minutes cannot be negative.");
        SpotType = spotType;
        HourlyRate = hourlyRate;
        DailyCap = dailyCap;
        FreeMinutes = freeMinutes;
        EffectiveFromUtc = effectiveFromUtc;
    }

    public SpotType SpotType { get; private set; }
    public decimal HourlyRate { get; private set; }
    public decimal DailyCap { get; private set; }
    public int FreeMinutes { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }

    public PricingTerms ToTerms() => new(HourlyRate, DailyCap, FreeMinutes);
}

public class LostTicketFee : Entity
{
    private LostTicketFee() { }

    public LostTicketFee(decimal amount, DateTime effectiveFromUtc)
    {
        if (amount < 0) throw new DomainException("Lost ticket fee cannot be negative.");
        Amount = amount;
        EffectiveFromUtc = effectiveFromUtc;
    }

    public decimal Amount { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
}

public class Payment : Entity
{
    private Payment() { }

    public Payment(Guid sessionId, decimal amount, string? idempotencyKey, DateTime paidAtUtc)
    {
        if (amount < 0) throw new DomainException("Payment amount cannot be negative.");
        SessionId = sessionId;
        Amount = amount;
        Method = PaymentMethod.Mock;
        Status = PaymentStatus.Paid;
        IdempotencyKey = idempotencyKey;
        PaidAtUtc = paidAtUtc;
    }

    public Guid SessionId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTime PaidAtUtc { get; private set; }
}

public class Receipt : Entity
{
    private Receipt() { }

    public Receipt(Guid sessionId, string number, decimal amount, string breakdownJson, DateTime issuedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        SessionId = sessionId;
        Number = number;
        Amount = amount;
        BreakdownJson = breakdownJson;
        IssuedAtUtc = issuedAtUtc;
    }

    public Guid SessionId { get; private set; }
    public string Number { get; private set; } = null!;
    public decimal Amount { get; private set; }

    /// <summary>Snapshot of how the fee was computed, so later price changes never alter old receipts.</summary>
    public string BreakdownJson { get; private set; } = null!;

    public DateTime IssuedAtUtc { get; private set; }
}

public class AuditLog : Entity
{
    private AuditLog() { }

    public AuditLog(Guid? actorUserId, string action, string entityType, string entityId, string? beforeJson, string? afterJson, DateTime timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ActorUserId = actorUserId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        TimestampUtc = timestampUtc;
    }

    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTime TimestampUtc { get; private set; }
}
