namespace Skanyxx.Core.Platform.Studio;

/// <summary>A proposal running in kagent under its preview name: labelled as a preview, so never in Chat (D030, D557).</summary>
public sealed record PreviewAgent(string Namespace, string Name, bool Ready);
