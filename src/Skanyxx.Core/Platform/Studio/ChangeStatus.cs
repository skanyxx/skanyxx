using System.Text.Json.Serialization;

namespace Skanyxx.Core.Platform.Studio;

[JsonConverter(typeof(JsonStringEnumConverter<ChangeStatus>))]
public enum ChangeStatus
{
    Open,
    Merged,
    Closed
}
