using System.Text.Json;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

public sealed record Tokens(string AccessToken, string RefreshToken, long ExpiresIn)
{
    public static Tokens From(JsonElement json) => new(
        json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!, json.GetProperty("expiresIn").GetInt64());
}
