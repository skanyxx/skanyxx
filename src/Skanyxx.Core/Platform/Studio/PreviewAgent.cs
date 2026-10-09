namespace Skanyxx.Core.Platform.Studio;

/// <summary>A proposal running in kagent under its preview name: never labelled merged, so never in Chat (D030, D031).</summary>
public sealed record PreviewAgent(string Namespace, string Name, bool Ready);
