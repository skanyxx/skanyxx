using System.Text.Json.Serialization;

namespace Skanyxx.Core.Models;

public class A2AMessagePart
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class A2AMessage
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "message";

    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("parts")]
    public List<A2AMessagePart> Parts { get; set; } = new();

    [JsonPropertyName("contextId")]
    public string ContextId { get; set; } = string.Empty;
}

public class A2AParams
{
    [JsonPropertyName("message")]
    public A2AMessage Message { get; set; } = new();
}

public class A2ARequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "message/stream";

    [JsonPropertyName("params")]
    public A2AParams Params { get; set; } = new();

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class A2AResponseMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("parts")]
    public List<A2AMessagePart>? Parts { get; set; }
}

public class A2AResponseStatus
{
    [JsonPropertyName("message")]
    public A2AResponseMessage? Message { get; set; }
}

public class A2AResult
{
    [JsonPropertyName("status")]
    public A2AResponseStatus? Status { get; set; }
}

public class A2AResponse
{
    [JsonPropertyName("result")]
    public A2AResult? Result { get; set; }
}
