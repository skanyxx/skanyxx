using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>Nothing to check (no input); one validator per request all the same.</summary>
internal sealed class StudioRepoQueryValidator : AbstractValidator<StudioRepoQuery>;
