using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Client errors become ProblemDetails with the right status instead of a 500:
/// a <see cref="ValidationException"/> from the MediatR pipeline → one 400 with every error keyed by field;
/// a <see cref="BadHttpRequestException"/> from Kestrel (e.g. body over the size limit) → its own status (413, …).
/// </summary>
public sealed class RequestProblemMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex) when (!context.Response.HasStarted)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            await Results.ValidationProblem(errors).ExecuteAsync(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            await Results.Problem(ex.Message, statusCode: ex.StatusCode).ExecuteAsync(context);
        }
    }
}
