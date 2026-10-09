namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// A <see cref="PrivilegesRevoked"/> handler could not finish (D162). The message is for the owner who made the change —
/// what did not happen, never a secret — and the API answers it as a <c>500</c> problem detail, the People page as its
/// error line. The change itself is saved; repeating it retries.
/// </summary>
public sealed class RevocationFailedException(string message, Exception? inner = null) : Exception(message, inner);
