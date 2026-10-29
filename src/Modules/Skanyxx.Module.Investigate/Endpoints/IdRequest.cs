using FastEndpoints;

namespace Skanyxx.Module.Investigate.Endpoints;

public class IdRequest
{
    [DontBind(Source.QueryParam)]
    public Guid Id { get; set; }
}
