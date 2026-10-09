using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// Runs the proposal in kagent as <c>preview-&lt;pr&gt;-&lt;name&gt;</c> (D030): not merged, memory search on
/// <c>company</c> only, never upsert (D032), never acting for users.
/// </summary>
public sealed record StartPreviewCommand(StudioUser User, int Number) : IRequest<Outcome<PreviewAgent>>;
