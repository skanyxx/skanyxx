using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Reports;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>The JSON and PDF downloads of a finished run.</summary>
public sealed partial class ReportTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    [Fact]
    public async Task TheJsonReport_HasEveryStepInTreeOrder_WithTimingsAndAnswers()
    {
        var workflow = await App.CreateAsync(Builder, "Report me",
            [Agent("s1", Researcher, "{{input}}", "Look"), Group("g1", GroupMode.Parallel, Agent("s2", Analyst, "a"), Agent("s3", Writer, "b"))]);
        var run = await App.StartAsync(Builder, workflow.Id, "the question");
        await App.WaitForAsync(run.Id, RunState.Succeeded);

        var response = await App.Client(Builder).GetAsync($"/api/investigate/runs/{run.Id}/report.json", Ct);
        var report = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.GetValues("Content-Disposition").First());
        Assert.Equal("succeeded", report.GetProperty("state").GetString());
        Assert.Equal("the question", report.GetProperty("input").GetString());
        Assert.Equal($"personal:{Builder}/investigation-{run.Id:N}", report.GetProperty("card").GetString());
        var steps = report.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["s1", "g1", "s2", "s3"], steps.Select(s => s.GetProperty("id").GetString()));
        Assert.Equal([1, 1, 2, 2], steps.Select(s => s.GetProperty("depth").GetInt32()));
        Assert.Equal("parallel", steps[1].GetProperty("mode").GetString());
        Assert.Equal($"{Researcher} says: {Prompts.Input("the question")}", Prompts.Plain(steps[0].GetProperty("output").GetString()!));
        Assert.All(steps, s => Assert.True(s.GetProperty("durationMs").GetInt64() >= 0));
    }

    [Fact]
    public async Task ThePdfReport_IsAWellFormedPdf_WithTheFindingInIt()
    {
        var workflow = await App.CreateAsync(Builder, "Pdf (one)", [Agent("s1", Researcher, "{{input}}", "Lookup step")]);
        var run = await App.StartAsync(Builder, workflow.Id, "Ünïcode ✓ question");
        await App.WaitForAsync(run.Id, RunState.Succeeded);

        var response = await App.Client(Builder).GetAsync($"/api/investigate/runs/{run.Id}/report.pdf", Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        AssertWellFormed(bytes);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains(@"(Investigation: Pdf \(one\)) Tj", text);
        Assert.Contains("Lookup step", text);
        Assert.Contains("(Ünïcode ? question) Tj", text);
    }

    [Fact]
    public void ALongReport_BreaksIntoPages_AndAVeryLongAnswerIsCutWithANote()
    {
        var output = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"line {i} " + new string('x', 150)));
        var report = new RunReport(Guid.NewGuid(), Guid.NewGuid(), "Long", "in", "succeeded", null, "bea", DateTime.UtcNow, null, null, null,
            [new ReportStep("s1", "Step", "agent", 1, Researcher, null, "succeeded", null, null, null, output, null)]);

        var bytes = PdfReport.Render(report);

        AssertWellFormed(bytes);
        var count = int.Parse(PageCount().Match(Encoding.Latin1.GetString(bytes)).Groups[1].Value);
        Assert.True(count > 3, $"{count} pages");
        Assert.Contains("[cut here; the JSON report has the whole answer]", Encoding.Latin1.GetString(bytes));
    }

    /// <summary>Every xref offset points at its object, and startxref at the table.</summary>
    private static void AssertWellFormed(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        var startxref = long.Parse(StartXref().Match(text).Groups[1].Value);
        Assert.StartsWith("xref", text[(int)startxref..]);
        var offsets = XrefEntry().Matches(text[(int)startxref..]).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(offsets);
        for (var i = 0; i < offsets.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..]);
    }

    [GeneratedRegex(@"startxref\n(\d+)\n")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"(\d{10}) 00000 n ")]
    private static partial Regex XrefEntry();

    [GeneratedRegex(@"/Type /Pages /Count (\d+)")]
    private static partial Regex PageCount();
}
