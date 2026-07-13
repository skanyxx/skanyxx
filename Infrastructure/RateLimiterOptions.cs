namespace SkanyxxWeb.Infrastructure;

public class RateLimiterOptions
{
    public int PermitLimit { get; set; } = 100;
    public int QueueLimit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
