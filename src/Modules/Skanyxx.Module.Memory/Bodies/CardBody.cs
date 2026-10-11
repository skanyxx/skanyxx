namespace Skanyxx.Module.Memory.Bodies;

/// <summary>A card's full body as UTF-8 text, for a person who may open the card (D183). The caller disposes it.</summary>
public sealed record CardBody(Stream Content, long? Length);
