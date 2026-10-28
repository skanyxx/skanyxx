using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Features.Library;

internal sealed class BrowseLibraryValidator : AbstractValidator<BrowseLibraryQuery>
{
    private static readonly string[] Types = Enum.GetNames<CardType>().Select(n => n.ToLowerInvariant()).ToArray();
    private static readonly string[] Origins = Enum.GetNames<CardOrigin>().Select(n => n.ToLowerInvariant()).ToArray();

    public BrowseLibraryValidator()
    {
        RuleFor(q => q.User).NotNull().SetValidator(new LibraryUserValidator());
        RuleFor(q => q.Text).MaximumLength(CardFormat.MaxQueryLength).NoNulCharacters();
        RuleFor(q => q.Scope).Must(s => Scope.TryParse(s, out _)).When(q => !string.IsNullOrEmpty(q.Scope)).WithMessage("Invalid scope.");
        RuleFor(q => q.Filter!.Ticket).Must(CardTickets.IsValid).When(q => !string.IsNullOrEmpty(q.Filter?.Ticket))
            .WithMessage("Ticket must be a ticket key like 'SDB-58'.");
        RuleFor(q => q.Filter!.Keyword).Must(CardLinks.IsKeyword).When(q => !string.IsNullOrEmpty(q.Filter?.Keyword))
            .WithMessage("Keyword must be a lowercase word.");
        RuleFor(q => q.Filter!.Type).Must(Types.Contains).When(q => !string.IsNullOrEmpty(q.Filter?.Type))
            .WithMessage($"Type must be one of: {string.Join(", ", Types)}.");
        RuleFor(q => q.Filter!.Origin).Must(Origins.Contains).When(q => !string.IsNullOrEmpty(q.Filter?.Origin))
            .WithMessage($"Origin must be one of: {string.Join(", ", Origins)}.");
        RuleFor(q => q.Filter!.Agent).Must(Scope.IsValidId).When(q => !string.IsNullOrEmpty(q.Filter?.Agent))
            .WithMessage("Invalid agent id.");
        RuleFor(q => q.Filter!.Offset).InclusiveBetween(0, LibraryFilter.MaxOffset).When(q => q.Filter is not null);
    }
}
