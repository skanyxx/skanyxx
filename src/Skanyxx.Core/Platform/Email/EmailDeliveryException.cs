namespace Skanyxx.Core.Platform.Email;

/// <summary>The SMTP server could not be reached or refused the message. The message never names the body or the credentials.</summary>
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);
