using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>One card for the library, with what this person may do to it.</summary>
public sealed record OpenCardQuery(LibraryUser User, string Scope, string Key) : IRequest<Outcome<LibraryCard>>;
