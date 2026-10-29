using System.Security.Claims;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary>Field names go as sent: two that differ only by spaces are the validator's 400, not a collision here.</summary>
internal static class ModelCommands
{
    public static SaveModelConfigCommand Save(ClaimsPrincipal user, string ns, string name, bool create, ModelForm form) => new(
        Caller.UserId(user)!, ns.Trim(), name.Trim(), create, form.Provider?.Trim() ?? "", form.Model?.Trim() ?? "",
        (form.Params ?? []).ToDictionary(p => p.Key, p => p.Value?.Trim() ?? ""), form.ApiKey?.Trim(), form.NoApiKey);
}
