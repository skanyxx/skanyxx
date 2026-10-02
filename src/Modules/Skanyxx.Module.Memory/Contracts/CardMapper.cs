using Riok.Mapperly.Abstractions;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Contracts;

[Mapper(EnumMappingStrategy = EnumMappingStrategy.ByName, EnumNamingStrategy = EnumNamingStrategy.CamelCase)]
public static partial class CardMapper
{
    [MapperIgnoreSource(nameof(Card.Id))]
    [MapperIgnoreSource(nameof(Card.Search))]
    public static partial CardDto ToDto(this Card card);

    [MapperIgnoreSource(nameof(Card.Id))]
    [MapperIgnoreSource(nameof(Card.Search))]
    [MapperIgnoreSource(nameof(Card.Status))]
    [MapperIgnoreSource(nameof(Card.Body))]
    [MapperIgnoreSource(nameof(Card.Source))]
    [MapperIgnoreSource(nameof(Card.LiftedFromId))]
    public static partial CardHit ToHit(this Card card);
}
