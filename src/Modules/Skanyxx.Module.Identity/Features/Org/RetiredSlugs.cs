using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>Slugs of deleted departments and teams (D351): retired in the delete's transaction, refused at create.</summary>
internal static class RetiredSlugs
{
    public static Task<bool> IsRetiredAsync(AccountsDbContext db, string kind, string slug, CancellationToken ct) =>
        db.RetiredOrgSlugs.AnyAsync(r => r.Kind == kind && r.Slug == slug, ct);

    public static Task RetireAsync(AccountsDbContext db, string kind, string slug, string actorId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_org_retired_slugs ("Kind", "Slug", "RetiredBy", "RetiredUtc")
            VALUES ({kind}, {slug}, {actorId}, {now}) ON CONFLICT DO NOTHING
            """, ct);

    public static string Refusal(string kind, string slug) =>
        $"The {kind} slug '{slug}' belonged to a deleted {kind}. Slugs are never reused (memory keys on them); choose another.";
}
