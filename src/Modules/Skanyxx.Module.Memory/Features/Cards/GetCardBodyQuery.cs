using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Bodies;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>A card's full body, for a person who may open the card (D183). Never an MCP tool (D014).</summary>
public sealed record GetCardBodyQuery(MemoryCaller Caller, string Scope, string Key) : IRequest<Outcome<CardBody>>;
