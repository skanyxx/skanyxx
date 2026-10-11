using System.Text;
using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class UpsertCardValidator : AbstractValidator<UpsertCardCommand>
{
    private static readonly string[] Types = Enum.GetNames<CardType>().Select(n => n.ToLowerInvariant()).ToArray();

    public UpsertCardValidator(IOptions<MemoryOptions> options)
    {
        var store = options.Value.ObjectStore;
        RuleFor(c => c.Caller).SetValidator(new CallerValidator());
        RuleFor(c => c.Scope).Must(s => Domain.Scope.TryParse(s, out _))
            .WithMessage("Scope must be 'company' or 'personal|team|department:<id>'.");
        RuleFor(c => c.Key).Must(CardKey.IsValid)
            .WithMessage("Key must be a lowercase slug (a-z, 0-9, '-'), at most 80 characters.");
        RuleFor(c => c.Version).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Type).Must(Types.Contains).WithMessage($"Type must be one of: {string.Join(", ", Types)}.");
        RuleFor(c => c.What).NotEmpty().MaximumLength(CardLimits.What).NoNulCharacters().WellFormedUtf16();
        RuleFor(c => c.Why).NotEmpty().MaximumLength(CardLimits.Why).NoNulCharacters().WellFormedUtf16();
        // With an object store a large body leaves Postgres (D180), so the cap is the store's, in UTF-8 bytes.
        if (store.Enabled)
            RuleFor(c => c.Body).Must(b => b is null || Encoding.UTF8.GetByteCount(b) <= store.MaxBodyBytes)
                .WithMessage($"Body must be at most {store.MaxBodyBytes} bytes (UTF-8).").NoNulCharacters().WellFormedUtf16();
        else
            RuleFor(c => c.Body).MaximumLength(CardLimits.Body).NoNulCharacters().WellFormedUtf16();
        RuleFor(c => c.Source).MaximumLength(CardLimits.Source).NoNulCharacters().WellFormedUtf16();
    }
}
