using System.Security.Claims;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

internal static class ModelCommands
{
    public static SaveModelConfigCommand Save(ClaimsPrincipal user, string ns, string name, bool create, ModelForm form) => new(
        Caller.UserId(user)!, ns.Trim(), name.Trim(), create, form.Provider?.Trim() ?? "", form.Model?.Trim() ?? "",
        (form.Params ?? []).ToDictionary(p => p.Key.Trim(), p => p.Value?.Trim() ?? ""), form.ApiKey?.Trim(), form.NoApiKey);
}
