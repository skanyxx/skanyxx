using Skanyxx.Core.Data;

namespace Skanyxx.Module.Settings.Controllers;

/// <summary>A kagent connection as the API shows it: never the token, only whether one is set.</summary>
public sealed record ConnectionView(int Id, string Name, string BaseUrl, int Port, string Protocol, bool IsDefault, DateTime CreatedAt, bool HasToken)
{
    public static ConnectionView From(KAgentConnection c) =>
        new(c.Id, c.Name, c.BaseUrl, c.Port, c.Protocol, c.IsDefault, c.CreatedAt, !string.IsNullOrEmpty(c.Token));
}
