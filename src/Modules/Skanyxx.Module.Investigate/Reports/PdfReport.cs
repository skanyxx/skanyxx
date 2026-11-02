using System.Globalization;
using System.Text;

namespace Skanyxx.Module.Investigate.Reports;

/// <summary>
/// A report as a plain PDF 1.4 written here (D345): the standard Helvetica and Courier fonts (no embedding), WinAnsi
/// text, one content stream per A4 page, uncompressed. Text outside Latin-1 prints as '?'; the JSON report has it
/// exactly. No library: the ones that lay out PDFs are either not free for commercial use or far more than a report of
/// headings and wrapped text needs.
/// </summary>
public static class PdfReport
{
    private const float PageWidth = 595, PageHeight = 842, Margin = 50;
    private const int MaxOutputPerStep = 20_000;

    private sealed record Line(string Font, float Size, string Text, float Indent, float Gap = 0);

    public static byte[] Render(RunReport report)
    {
        var lines = new List<Line>();
        Add(lines, "F2", 16, $"Investigation: {report.Workflow}", 0, 6);
        Add(lines, "F1", 10, $"State: {report.State}    Started by: {report.StartedBy}", 0);
        Add(lines, "F1", 10, $"Created: {Time(report.CreatedAt)}    Started: {Time(report.StartedAt)}    Ended: {Time(report.EndedAt)}", 0);
        if (report.Failure is not null)
            Add(lines, "F1", 10, $"Why it failed: {report.Failure}", 0);
        if (report.Card is not null)
            Add(lines, "F1", 10, $"Memory card: {report.Card}", 0);
        Add(lines, "F2", 12, "Input", 0, 10);
        Add(lines, "F3", 9, report.Input, 0);

        foreach (var step in report.Steps)
        {
            var indent = (step.Depth - 1) * 14f;
            var what = step.Kind == "group" ? $"group, {step.Mode}" : step.Agent;
            Add(lines, "F2", 11, $"{step.Title}  ({what})", indent, 10);
            Add(lines, "F1", 9, $"{step.State}{(step.DurationMs is { } ms ? $", {ms / 1000.0:0.0} s" : "")}", indent);
            if (step.Error is not null)
                Add(lines, "F1", 9, $"Error: {step.Error}", indent);
            if (step.KagentReason is not null)
                Add(lines, "F3", 9, $"kagent said: {step.KagentReason}", indent);
            if (step.Output is not null)
                Add(lines, "F3", 9, step.Output.Length > MaxOutputPerStep
                    ? step.Output[..MaxOutputPerStep] + "\n[cut here; the JSON report has the whole answer]"
                    : step.Output, indent);
        }
        return Write(Paginate(lines));
    }

    private static string Time(DateTime? value) =>
        value is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : "-";

    /// <summary>Wrapped to the page by an average glyph width: exact for Courier, close enough for Helvetica.</summary>
    private static void Add(List<Line> lines, string font, float size, string text, float indent, float gap = 0)
    {
        var perChar = size * (font == "F3" ? 0.6f : 0.55f);
        var max = Math.Max(10, (int)((PageWidth - 2 * Margin - indent) / perChar));
        var first = true;
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            foreach (var piece in Wrap(paragraph, max))
            {
                lines.Add(new Line(font, size, piece, indent, first ? gap : 0));
                first = false;
            }
    }

    private static IEnumerable<string> Wrap(string text, int max)
    {
        if (text.Length == 0)
        {
            yield return "";
            yield break;
        }
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            var rest = word;
            while (rest.Length > max)
            {
                if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                yield return rest[..max];
                rest = rest[max..];
            }
            if (line.Length > 0 && line.Length + 1 + rest.Length > max) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(rest);
        }
        yield return line.ToString();
    }

    private static List<string> Paginate(List<Line> lines)
    {
        var pages = new List<string>();
        var page = new StringBuilder();
        var y = PageHeight - Margin;
        foreach (var line in lines)
        {
            var height = line.Size * 1.35f + line.Gap;
            if (y - height < Margin && page.Length > 0)
            {
                pages.Add(page.ToString());
                page.Clear();
                y = PageHeight - Margin;
            }
            y -= height;
            page.Append(CultureInfo.InvariantCulture, $"BT /{line.Font} {line.Size:0.##} Tf 1 0 0 1 {Margin + line.Indent:0.##} {y:0.##} Tm ({Escape(line.Text)}) Tj ET\n");
        }
        pages.Add(page.ToString());
        return pages;
    }

    /// <summary>A PDF string literal: Latin-1 printable characters, the delimiters escaped, "…" as "...", everything else '?'.</summary>
    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '(' or ')' or '\\') escaped.Append('\\').Append(c);
            else if (c == '\u2026') escaped.Append("..."); // a cut says so, also in Latin-1
            else if (c == '\t') escaped.Append("    ");
            else if (c is (>= ' ' and <= '~') or (>= ' ' and <= 'ÿ')) escaped.Append(c);
            else if (!char.IsControl(c)) escaped.Append('?');
        }
        return escaped.ToString();
    }

    private static byte[] Write(List<string> pages)
    {
        var latin1 = Encoding.Latin1;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", pages.Select((_, i) => $"{6 + 2 * i} 0 R"))}] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
        };
        for (var i = 0; i < pages.Count; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0} {PageHeight:0}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {7 + 2 * i} 0 R >>");
            objects.Add($"<< /Length {latin1.GetByteCount(pages[i])} >>\nstream\n{pages[i]}endstream");
        }

        using var pdf = new MemoryStream();
        void Put(string s) => pdf.Write(latin1.GetBytes(s));
        Put("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            Put($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Position;
        Put($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Put($"{offset:D10} 00000 n \n");
        Put($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }
}
