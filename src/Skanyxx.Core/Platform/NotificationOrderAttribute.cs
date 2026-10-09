namespace Skanyxx.Core.Platform;

/// <summary>
/// Where a notification handler runs among the others of the same notification under <see cref="AllHandlersPublisher"/>:
/// lower first, 0 when absent, registration order between equals. For handlers whose order matters — a quick local
/// revocation before a call to a slow external service (D160).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NotificationOrderAttribute(int order) : Attribute
{
    /// <summary>Local, database-only revocations: nothing external can hold them up.</summary>
    public const int Revocation = -100;

    /// <summary>Calls to an external service that may be slow or down.</summary>
    public const int External = 100;

    public int Order { get; } = order;
}
