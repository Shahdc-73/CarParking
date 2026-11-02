using Microsoft.AspNetCore.Mvc;
using ParkWise.Application.Common;

namespace ParkWise.Api.Infrastructure;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Maps an expected business failure to an RFC 7807 ProblemDetails response.</summary>
    protected ObjectResult FromError(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = new ProblemDetails { Status = status, Title = error.Message, Detail = error.Message };
        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    protected IActionResult ToActionResult(Result result)
        => result.IsSuccess ? NoContent() : FromError(result.Error!);

    protected IActionResult ToActionResult<T>(Result<T> result, Func<T, IActionResult>? onSuccess = null)
        => result.IsSuccess ? (onSuccess?.Invoke(result.Value) ?? Ok(result.Value)) : FromError(result.Error!);
}
