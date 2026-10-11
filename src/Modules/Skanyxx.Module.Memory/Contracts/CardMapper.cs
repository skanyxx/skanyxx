using Riok.Mapperly.Abstractions;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Contracts;

[Mapper(EnumMappingStrategy = EnumMappingStrategy.ByName, EnumNamingStrategy = EnumNamingStrategy.CamelCase)]
public static partial class CardMapper
{
    public static CardDto ToDto(this Card card) => card.ToDtoFields() with { BodyStored = BodyPointer.IsPointer(card.Source) };

    [MapperIgnoreSource(nameof(Card.Id))]
    [MapperIgnoreSource(nameof(Card.Search))]
    [MapperIgnoreTarget(nameof(CardDto.BodyStored))]
    private static partial CardDto ToDtoFields(this Card card);

    [MapperIgnoreSource(nameof(Card.Id))]
    [MapperIgnoreSource(nameof(Card.Search))]
    [MapperIgnoreSource(nameof(Card.Status))]
    [MapperIgnoreSource(nameof(Card.Body))]
    [MapperIgnoreSource(nameof(Card.Source))]
    [MapperIgnoreSource(nameof(Card.LiftedFromId))]
    public static partial CardHit ToHit(this Card card);
}
