using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Entities;

public class ParkingSession : Entity
{
    private ParkingSession() { }

    public ParkingSession(Guid vehicleId, Guid spotId, string ticketCode, DateTime checkInAtUtc, Guid checkedInBy, Guid? reservationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketCode);
        VehicleId = vehicleId;
        SpotId = spotId;
        TicketCode = ticketCode;
        CheckInAtUtc = checkInAtUtc;
        CheckedInBy = checkedInBy;
        ReservationId = reservationId;
        Status = SessionStatus.Active;
    }

    public Guid VehicleId { get; private set; }
    public Guid SpotId { get; private set; }
    public string TicketCode { get; private set; } = null!;
    public DateTime CheckInAtUtc { get; private set; }
    public DateTime? CheckOutAtUtc { get; private set; }
    public SessionStatus Status { get; private set; }
    public Guid CheckedInBy { get; private set; }
    public Guid? ReservationId { get; private set; }

    public Payment? Payment { get; private set; }
    public Receipt? Receipt { get; private set; }

    public bool IsActive => Status == SessionStatus.Active;

    public void CheckOut(DateTime checkOutAtUtc, bool lostTicket = false)
    {
        if (!IsActive)
            throw new DomainException("Session is already closed.");
        if (checkOutAtUtc < CheckInAtUtc)
            throw new DomainException("Check-out cannot be before check-in.");
        CheckOutAtUtc = checkOutAtUtc;
        Status = lostTicket ? SessionStatus.LostTicket : SessionStatus.Completed;
    }

    public void AttachSettlement(Payment payment, Receipt receipt)
    {
        if (Status == SessionStatus.Active)
            throw new DomainException("Cannot settle an active session.");
        Payment = payment;
        Receipt = receipt;
    }
}
