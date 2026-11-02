using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Api.Infrastructure;

/// <summary>Last line of defence: turns unexpected/low-level exceptions into RFC 7807 responses without leaking internals.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, code) = exception switch
        {
            DomainException => (StatusCodes.Status409Conflict, exception.Message, "domain.rule_violated"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified by someone else. Reload and try again.", "concurrency.conflict"),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                (StatusCodes.Status409Conflict, "That operation conflicts with existing data.", "data.unique_violation"),
            DbUpdateException { InnerException: SqlException { Number: 547 } } =>
                (StatusCodes.Status409Conflict, "That operation conflicts with related data.", "data.reference_conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server.error"),
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception");
        else logger.LogWarning(exception, "Handled exception: {Code}", code);

        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
