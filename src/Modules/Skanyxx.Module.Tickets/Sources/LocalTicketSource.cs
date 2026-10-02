using System.Text.Json;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Sources;

/// <summary>Tickets from a JSON file (an array of tickets): demos and tests without a Jira site.</summary>
public sealed class LocalTicketSource(IOptions<TicketsOptions> options) : ITicketSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<Ticket>> ListAsync(int limit, CancellationToken ct) =>
        (await ReadAsync(ct)).Take(limit).ToList();

    public async Task<Ticket> GetAsync(string key, CancellationToken ct) =>
        (await ReadAsync(ct)).SingleOrDefault(t => t.Key == key) ?? throw new TicketNotFoundException(key);

    private async Task<List<Ticket>> ReadAsync(CancellationToken ct)
    {
        await using var file = File.OpenRead(options.Value.LocalPath);
        return await JsonSerializer.DeserializeAsync<List<Ticket>>(file, Json, ct) ?? [];
    }
}
