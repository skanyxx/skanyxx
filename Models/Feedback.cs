using System.Text.Json.Serialization;

namespace SkanyxxWeb.Models;

public class Feedback
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("feedbackText")]
    public string FeedbackText { get; set; } = string.Empty;

    [JsonPropertyName("isPositive")]
    public bool IsPositive { get; set; }

    [JsonPropertyName("issueType")]
    public string? IssueType { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}
