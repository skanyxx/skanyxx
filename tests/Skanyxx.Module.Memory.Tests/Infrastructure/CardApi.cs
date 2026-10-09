using System.Net.Http.Json;
using System.Text.Json;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Contracts;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

public static class CardApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> PutCardAsync(
        this HttpClient client, string scope, string key, int version = 0, string type = "decision",
        string what = "We refund within 14 days", string why = "Finance policy X", string? body = null, string? source = null) =>
        client.PutAsJsonAsync($"/api/memory/cards/{scope}/{key}", new { version, type, what, why, body, source });

    public static Task<HttpResponseMessage> LiftAsync(this HttpClient client, string scope, string key, string targetScope) =>
        client.PostAsJsonAsync($"/api/memory/cards/{scope}/{key}/lift", new { targetScope });

    public static async Task<List<CardHit>> SearchAsync(this HttpClient client, string q) =>
        (await client.GetFromJsonAsync<List<CardHit>>($"/api/memory/cards?q={Uri.EscapeDataString(q)}", Json))!;

    public static Task<HttpResponseMessage> SetGrantsAsync(this HttpClient client, string agentId, params object[] grants) =>
        client.PutAsJsonAsync($"/api/memory/grants/{agentId}", new { grants });

    public static async Task<CardDto> CardAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CardDto>(Json))!;

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);
}
