namespace ParkWise.Domain.Enums;

public enum SpotType { Standard, Compact, EV, Disabled, Motorcycle }

public enum SpotStatus { Available, Occupied, Reserved, OutOfService }

public enum VehicleType { Car, SmallCar, ElectricCar, Motorcycle }

public enum SessionStatus { Active, Completed, LostTicket }

public enum ReservationStatus { Pending, Fulfilled, Expired, Cancelled }

public enum PaymentMethod { Mock }

public enum PaymentStatus { Paid }
