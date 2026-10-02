using System.Net;
using Microsoft.AspNetCore.Http;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// The caller's address for audit lines: the connection's, as the memory secret audit logs it (D085), so behind a
/// proxy it is the proxy and the actor id attributes. Null outside a request. Keeps handlers off the HTTP types.
/// </summary>
internal sealed class ClientAddress(IHttpContextAccessor http)
{
    public IPAddress? Current => http.HttpContext?.Connection.RemoteIpAddress;
}
