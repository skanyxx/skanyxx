namespace SkanyxxMaui.Models;

public class KagentConfig
{
    public string Protocol { get; set; } = "http";
    public string BaseUrl { get; set; } = "localhost";
    public int Port { get; set; } = 8083;
    public string? Token { get; set; }
}
