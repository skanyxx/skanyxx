using System.Text;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Bodies;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// Opens the card exactly as <see cref="GetCardQuery"/> does (its scope, then D103), so whoever may read the card reads its
/// body and nobody else; then streams the stored object, or the body Postgres holds.
/// </summary>
internal sealed class GetCardBodyHandler(IMediator mediator, CardBodies bodies) : IRequestHandler<GetCardBodyQuery, Outcome<CardBody>>
{
    public async Task<Outcome<CardBody>> Handle(GetCardBodyQuery query, CancellationToken ct)
    {
        var opened = await mediator.Send(new GetCardQuery(query.Caller, query.Scope, query.Key), ct);
        if (opened.Value is not { } card)
            return new Outcome<CardBody>(opened.Status, Message: opened.Message);

        var missing = $"Card '{query.Scope}/{query.Key}' has no body.";
        if (!BodyPointer.IsPointer(card.Source))
        {
            if (card.Body is null)
                return Outcome<CardBody>.NotFound(missing);
            var bytes = Encoding.UTF8.GetBytes(card.Body);
            return Outcome<CardBody>.Ok(new CardBody(new MemoryStream(bytes), bytes.Length));
        }
        if (!bodies.Enabled)
            return Outcome<CardBody>.Unavailable("This card's body is in an object store that is not configured (Memory:ObjectStore).");
        try
        {
            return await bodies.OpenAsync(card.Source, ct) is { } body ? Outcome<CardBody>.Ok(body) : Outcome<CardBody>.NotFound(missing);
        }
        catch (ObjectStoreUnavailableException)
        {
            return Outcome<CardBody>.Unavailable("The object store for card bodies did not answer.");
        }
    }
}
