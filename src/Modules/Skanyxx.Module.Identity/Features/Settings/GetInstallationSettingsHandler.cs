using MediatR;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Settings;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Settings;

internal sealed class GetInstallationSettingsHandler(AccountsDbContext db, IConfiguration configuration)
    : IRequestHandler<GetInstallationSettingsQuery, Outcome<IReadOnlyDictionary<string, string>>>
{
    public async Task<Outcome<IReadOnlyDictionary<string, string>>> Handle(GetInstallationSettingsQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyDictionary<string, string>>.Ok(await EffectiveSettings.ReadAsync(db, configuration, ct));
}
