using System.Diagnostics;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

/// <summary>
/// FastEndpoints binding errors (malformed JSON, wrong types) in the same shape as validation errors:
/// an <c>errors</c> object keyed by field and a W3C trace id. The framework's own messages echo raw input
/// and internal type names, so only a fixed message is returned.
/// </summary>
public static class BindingProblem
{
    public static object Create(List<ValidationFailure> failures, HttpContext context, int statusCode) =>
        new HttpValidationProblemDetails(failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, _ => new[] { "The value could not be read." }))
        {
            Status = statusCode,
            Extensions = { ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier }
        };
}
