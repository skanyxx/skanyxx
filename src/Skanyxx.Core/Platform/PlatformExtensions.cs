using System.Reflection;
using System.Runtime.ExceptionServices;
using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

public static class PlatformExtensions
{
    /// <summary>CQRS pipeline (MediatR + validation + logging) and REPR endpoints for the given module assemblies.</summary>
    public static IServiceCollection AddSkanyxxPlatform(this IServiceCollection services, IReadOnlyList<Assembly> moduleAssemblies)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IModule).Assembly);
            cfg.RegisterServicesFromAssemblies([.. moduleAssemblies]);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.NotificationPublisherType = typeof(AllHandlersPublisher);
        });
        services.AddValidatorsFromAssemblies(moduleAssemblies, includeInternalTypes: true);
        services.AddProblemDetails();
        services.AddExceptionHandler<RevocationFailedProblem>();
        services.AddFastEndpoints(o =>
        {
            o.Assemblies = moduleAssemblies;
            o.DisableAutoDiscovery = true;
        });
        return services;
    }

    /// <summary>
    /// Production error handling: API and MCP clients get ProblemDetails JSON, pages get the Razor error page.
    /// The two branches are disjoint — an outer <c>UseExceptionHandler(errorPage)</c> would catch API errors first.
    /// </summary>
    public static WebApplication UseSkanyxxErrorHandling(this WebApplication app, string errorPage)
    {
        app.UseWhen(IsApi, api => api.UseExceptionHandler());
        app.UseWhen(context => !IsApi(context), web => web.UseExceptionHandler(errorPage));
        return app;
    }

    private static bool IsApi(Microsoft.AspNetCore.Http.HttpContext context) => IsApi(context.Request.Path);

    internal static bool IsApi(Microsoft.AspNetCore.Http.PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/mcp");

    public static WebApplication UseSkanyxxPlatform(this WebApplication app, IEnumerable<IModule> modules)
    {
        app.UseMiddleware<RequestProblemMiddleware>();
        app.UseFastEndpoints(c =>
        {
            c.Errors.ResponseBuilder = BindingProblem.Create;
            c.Errors.ContentType = "application/problem+json";
            c.Errors.ProducesMetadataType = typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails);
            c.Binding.FormExceptionTransformer = FormBindingFailure;
        });
        foreach (var module in modules.OfType<IEndpointModule>())
            module.MapEndpoints(app);
        return app;
    }

    /// <summary>
    /// A form body FastEndpoints cannot read. Kestrel's own refusal (413 over the body cap) is a
    /// <see cref="BadHttpRequestException"/>, which is an <see cref="IOException"/>, so FastEndpoints' default would call it
    /// malformed (400): it goes on to <see cref="RequestProblemMiddleware"/> with its status, like a JSON body does.
    /// Anything else keeps the default: malformed data is a 400 under FastEndpoints' general errors key, other exceptions are
    /// not caught here.
    /// </summary>
    private static ValidationFailure FormBindingFailure(Exception e)
    {
        if (e is BadHttpRequestException or not (IOException or InvalidDataException))
            ExceptionDispatchInfo.Throw(e);
        return new ValidationFailure("GeneralErrors", "The request body contains malformed form data!");
    }
}
