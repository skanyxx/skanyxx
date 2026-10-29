using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary>Every quota endpoint: a signed-in person (FastEndpoints' default), CORS off, small bodies. Roles per endpoint, scopes in the handlers.</summary>
internal sealed class QuotasGroup : Group
{
    public const int MaxBodyBytes = 16 * 1024;

    public QuotasGroup() =>
        Configure("api/quotas", ep => ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))));
}
