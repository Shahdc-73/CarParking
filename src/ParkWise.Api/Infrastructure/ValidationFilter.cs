using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ParkWise.Api.Infrastructure;

/// <summary>
/// Runs the FluentValidation validator for every action argument that has one, before the action executes.
/// Keeps validation out of controllers and returns a standard 400 ValidationProblemDetails.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>();

        // Binding failures (malformed JSON, unknown enum value, wrong types, missing body) arrive as ModelState errors with a
        // null argument. Since we turned off the automatic 400, we must report them here or the action would run with null.
        foreach (var (key, entry) in context.ModelState.Where(e => e.Value?.Errors.Count > 0))
        {
            var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
            name = name.Length == 0 || name == "$" ? "" : char.ToLowerInvariant(name[0]) + name[1..];
            foreach (var _ in entry!.Errors)
                (errors.TryGetValue(name, out var list) ? list : errors[name] = [])
                    .Add("The request body or value is invalid."); // deliberately generic: raw parser messages can leak internals
        }

        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                var key = string.IsNullOrEmpty(failure.PropertyName) ? "" : char.ToLowerInvariant(failure.PropertyName[0]) + failure.PropertyName[1..];
                (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
            })
            { ContentTypes = { "application/problem+json" } };
            return;
        }

        await next();
    }
}
