using System.Security.Claims;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Endpoints;

/// <summary>A REST caller: always a human, identified by the authenticated principal only.</summary>
internal static class HumanCaller
{
    public static MemoryCaller From(ClaimsPrincipal user) => MemoryCaller.For(LibraryUser.From(user));
}
