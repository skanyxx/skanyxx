using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>Nothing to check (the input is the external cookie); present because every request has exactly one validator.</summary>
internal sealed class CompleteEntraSignInValidator : AbstractValidator<CompleteEntraSignInCommand>;
