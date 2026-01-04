namespace SkanyxxMaui.Models;

public class AlertEventTypeBreakdown
{
    public int PodRestart { get; set; }
    public int PodPending { get; set; }
    public int OomKill { get; set; }
    public int ProbeFailed { get; set; }
}
