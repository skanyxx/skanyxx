using System.Text.Json.Nodes;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Features.AuditExplorer;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>D275: the explorer's pure parts — redaction, bucket sizes, chips, CSV cells, admin-change kinds.</summary>
public sealed class AuditExplorerUnitTests
{
    [Fact]
    public void Redaction_MasksByName_AndByShape_AndCutsLongText()
    {
        var details = JsonNode.Parse("""
            {"Password": "p", "client_secret": "s", "githubToken": "t", "API-Key": "k", "connectionString": "c", "set-cookie": "x",
             "systemPrompt": "sp", "messages": [{"content": "hi"}], "tokenCheck": "valid", "secretChange": "replaced",
             "note": "use sk-abcdefghijklmnopqrstuv here", "count": 3, "list": ["Bearer abcdefghijklmnopqrstuvwxyz"],
             "long": "%%LONG%%"}
            """.Replace("%%LONG%%", new string('y', 300)))!;

        var redacted = AuditRedactor.Redact(details)!.AsObject();

        foreach (var name in new[] { "Password", "client_secret", "githubToken", "API-Key", "connectionString", "set-cookie", "systemPrompt", "messages" })
            Assert.Equal(SecretText.Mask, redacted[name]!.GetValue<string>());
        Assert.Equal("valid", redacted["tokenCheck"]!.GetValue<string>());
        Assert.Equal("replaced", redacted["secretChange"]!.GetValue<string>());
        Assert.Equal($"use {SecretText.Mask} here", redacted["note"]!.GetValue<string>());
        Assert.Equal(3, redacted["count"]!.GetValue<int>());
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", redacted["list"]!.ToJsonString());
        Assert.EndsWith("[cut, 300 chars]", redacted["long"]!.GetValue<string>());
        Assert.Equal("p", details["Password"]!.GetValue<string>()); // the input is not changed
    }

    [Theory]
    [InlineData(15 * 60, 10)]
    [InlineData(60 * 60, 60)]
    [InlineData(24 * 3600, 1800)]
    [InlineData(7 * 86400, 3 * 3600)]
    [InlineData(30 * 86400, 12 * 3600)]
    [InlineData(366 * 86400, 7 * 86400)]
    public void TheBucket_KeepsAtMostAHundredBars(int rangeSeconds, int bucketSeconds)
    {
        var from = new DateTimeOffset(2026, 10, 28, 10, 7, 13, TimeSpan.Zero);
        var to = from.AddSeconds(rangeSeconds);

        var interval = AuditInterval.For(from, to);
        var starts = AuditInterval.Starts(from, to, interval).ToList();

        Assert.Equal(TimeSpan.FromSeconds(bucketSeconds), interval);
        Assert.InRange(starts.Count, 1, AuditInterval.MaxBuckets + 1);
        Assert.True(starts[0] <= from && from < starts[0] + interval);
        Assert.True(starts[^1] < to && to <= starts[^1] + interval);
    }

    [Theory]
    [InlineData("action:person.disabled", "action", "person.disabled")]
    [InlineData("ip:2001:db8::1", "ip", "2001:db8::1")]
    [InlineData("actor:(none)", "actor", null)]
    [InlineData("nocolon", "nocolon", "")]
    public void AChip_SplitsAtTheFirstColon(string chip, string field, string? value)
    {
        var parsed = AuditFieldFilter.Parse(chip, exclude: true);

        Assert.Equal(new AuditFieldFilter(field, value, true), parsed);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-2", "\"'-2\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("person.disabled", "\"person.disabled\"")]
    [InlineData(null, "\"\"")]
    public void ACsvCell_IsQuoted_AndNeverAFormula(string? value, string cell) => Assert.Equal(cell, AuditCsv.Cell(value));

    [Theory]
    [InlineData("person.roles_changed", "roles")]
    [InlineData("invite.created", "people")]
    [InlineData("agent.deleted", "agents")]
    [InlineData("agent.update_requested", null)]
    [InlineData("model.created", "models")]
    [InlineData("toolserver.deleted", "toolServers")]
    [InlineData("org.team_created", "org")]
    [InlineData("entra.settings_saved", "signInSettings")]
    [InlineData("signin.succeeded", null)]
    public void AdminChanges_AreSortedByKind(string action, string? category) => Assert.Equal(category, AdminChangeCategories.Of(action));
}
