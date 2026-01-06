namespace SkanyxxWeb.Models;

public class CreateMemoryRequest
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
