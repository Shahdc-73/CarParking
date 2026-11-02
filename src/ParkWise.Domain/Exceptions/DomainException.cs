namespace ParkWise.Domain.Exceptions;

/// <summary>A business rule was violated. The API layer maps this to a 409/422 ProblemDetails.</summary>
public class DomainException(string message) : Exception(message);
