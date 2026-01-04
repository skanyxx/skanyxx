namespace SkanyxxMaui.Models;

public class Connector
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Port { get; set; } = 8083;
    public string Protocol { get; set; } = "http";
    public string? Token { get; set; }
    public string Environment { get; set; } = "local";
    public ConnectorStatus Status { get; set; } = ConnectorStatus.Unknown;
    public DateTime? LastChecked { get; set; }
}
