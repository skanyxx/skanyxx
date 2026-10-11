namespace Skanyxx.Module.Memory.Bodies;

/// <summary>The bucket could not be reached or refused the request; the write it belonged to did not happen.</summary>
internal sealed class ObjectStoreUnavailableException(Exception inner) : Exception("The object store did not answer.", inner);
