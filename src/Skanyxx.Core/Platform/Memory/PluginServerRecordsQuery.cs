using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>Every plugin server memory has a record of, without the values.</summary>
public sealed record PluginServerRecordsQuery : IRequest<Outcome<IReadOnlyList<PluginServerRecord>>>;
