using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <param name="DefaultRef">The default model's ref (<c>KAgent:ModelConfig</c>), listed or not.</param>
/// <param name="CanWrite">The caller may create, edit and delete (the owner); cosmetic, every write checks again.</param>
internal sealed record ModelList(string DefaultRef, bool CanWrite, IReadOnlyList<ModelConfigDetail> Models);
