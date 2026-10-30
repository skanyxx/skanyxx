namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>
/// The configured price of the agent's model (<c>Analytics:Prices</c>, D549), for Chat's cost estimate: per million
/// model tokens in and out. Null on <see cref="ChatAgentDetails"/> when no price is set for <paramref name="Model"/>.
/// </summary>
internal sealed record ChatAgentPrice(string Model, decimal InputPerMillion, decimal OutputPerMillion, string Currency);
