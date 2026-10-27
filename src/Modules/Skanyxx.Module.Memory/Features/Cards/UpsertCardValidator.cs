using System.Text;
using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Memory;
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
        RuleFor(c => c.Tickets).Must(t => t is null || (t.Count <= CardLimits.Tickets && t.All(CardTickets.IsValid) && t.Distinct().Count() == t.Count))
            .WithMessage($"Tickets must be at most {CardLimits.Tickets} distinct ticket keys like 'SDB-58'.");
        RuleFor(c => c.Links).Must(l => l is null || (l.Count <= CardLimits.Links && l.All(CardLinks.IsValid) && l.Distinct().Count() == l.Count))
            .WithMessage($"Links must be at most {CardLimits.Links} distinct 'scope/key', 'chat:<id>', 'run:<id>' or http(s) URLs " +
                         $"of at most {CardLimits.LinkLength} characters.");
        RuleFor(c => c.Keywords).Must(k => k is null || (k.Count <= CardLimits.Keywords && k.All(CardLinks.IsKeyword) && k.Distinct().Count() == k.Count))
            .WithMessage($"Keywords must be at most {CardLimits.Keywords} distinct lowercase words (a-z, 0-9, '-'), " +
                         $"at most {CardLimits.KeywordLength} characters each.");
        // requirements.md §1.4: a card is read back by people and agents, so it must not carry a credential.
        RuleFor(c => c).Must(c => !new[] { c.What, c.Why, c.Body, c.Source }.Concat(c.Links ?? []).Any(SecretText.HasCredential))
            .OverridePropertyName("card")
            .WithMessage("The card looks like it contains a credential (a key, token or password); remove it and save again.");
        RuleFor(c => c.ViaAgent).Must(Domain.Scope.IsValidId).When(c => c.ViaAgent is not null).WithMessage("Invalid agent id.");
    }
}
