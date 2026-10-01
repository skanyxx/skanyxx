using System.Net;
using Microsoft.AspNetCore.Http;

namespace Skanyxx.Module.Memory.Features;

/// <summary>
/// The caller's address for audit lines written from a handler: the connection's, as the endpoint audit lines log it
/// (D085). Null outside a request. Keeps handlers off the HTTP types.
/// </summary>
internal sealed class ClientAddress(IHttpContextAccessor http)
{
    public IPAddress? Current => http.HttpContext?.Connection.RemoteIpAddress;
}
