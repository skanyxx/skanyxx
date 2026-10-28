namespace Skanyxx.Module.Memory.Domain;

/// <summary>How a card came to be: written by someone, or extracted from a chat or a ticket run.</summary>
public enum CardOrigin
{
    Manual,
    Chat,
    Run
}
