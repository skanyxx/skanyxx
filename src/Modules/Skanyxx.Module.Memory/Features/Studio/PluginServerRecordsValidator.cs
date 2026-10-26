using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>No input; present so every request has its validator.</summary>
internal sealed class PluginServerRecordsValidator : AbstractValidator<PluginServerRecordsQuery>;
