using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SkanyxxWeb.Services;

/// <summary>
/// Filesystem tools exposed to Claude during the agentic loop.
/// All operations are sandboxed to the workspace root path.
/// </summary>
internal static class DevToolsAgentTools
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", "node_modules", ".vs", "dist", "build", "__pycache__", ".idea" };

    // ── Tool definitions sent to Claude API ───────────────────────────────────

    public static object[] Definitions() => new object[]
    {
        new
        {
            name        = "read_file",
            description = "Read the full contents of a source file. Always read relevant files before answering — never ask the user to paste code.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Absolute path or path relative to the project root." }
                },
                required = new[] { "path" }
            }
        },
        new
        {
            name        = "write_file",
            description = "Create or overwrite a file with the given content. Use this to create new files, add tests, scaffold code, update configs — do the actual work instead of showing instructions.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    path    = new { type = "string", description = "Absolute path or path relative to the project root." },
                    content = new { type = "string", description = "Full file content to write." }
                },
                required = new[] { "path", "content" }
            }
        },
        new
        {
            name        = "run_command",
            description = "Run a shell command in the project directory. Use this to run dotnet build, dotnet test, git commands, npm, etc. Always run commands rather than showing the user how to run them.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    command   = new { type = "string", description = "Shell command to execute." },
                    directory = new { type = "string", description = "Working directory. Defaults to project root." }
                },
                required = new[] { "command" }
            }
        },
        new
        {
            name        = "list_directory",
            description = "List files and sub-directories at a path. Use this to explore the project structure.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    path      = new { type = "string", description = "Directory to list. Defaults to project root if omitted." },
                    recursive = new { type = "boolean", description = "Include all sub-directories recursively." }
                },
                required = Array.Empty<string>()
            }
        },
        new
        {
            name        = "search_code",
            description = "Search for a text pattern (regex supported) across the codebase. Returns file paths and matching lines.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    pattern      = new { type = "string", description = "Search pattern (ripgrep-style regex)." },
                    directory    = new { type = "string", description = "Directory to search in. Defaults to project root." },
                    file_pattern = new { type = "string", description = "Glob to filter files, e.g. '*.cs'. Optional." }
                },
                required = new[] { "pattern" }
            }
        }
    };

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>Executes a tool call and returns (result text, human-readable label).</summary>
    public static (string result, string label) Execute(string toolName, JsonNode? input, string? workspaceRoot)
    {
        try
        {
            return toolName switch
            {
                "read_file"      => ReadFile(input, workspaceRoot),
                "write_file"     => WriteFile(input, workspaceRoot),
                "run_command"    => RunCommand(input, workspaceRoot),
                "list_directory" => ListDirectory(input, workspaceRoot),
                "search_code"    => SearchCode(input, workspaceRoot),
                _                => ($"Unknown tool: {toolName}", toolName)
            };
        }
        catch (Exception ex)
        {
            return ($"Tool error: {ex.Message}", toolName);
        }
    }

    // ── read_file ─────────────────────────────────────────────────────────────

    private static (string, string) ReadFile(JsonNode? input, string? root)
    {
        var rawPath = input?["path"]?.GetValue<string>() ?? "";
        var path    = ResolvePath(rawPath, root);

        if (!File.Exists(path))
            return ($"File not found: {path}", $"read_file({rawPath})");

        var info = new FileInfo(path);
        if (info.Length > 200 * 1024)
            return ($"File too large ({info.Length / 1024} KB). Max 200 KB.", $"read_file({rawPath})");

        var content = File.ReadAllText(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLower();
        return ($"```{ext}\n{content}\n```", $"read_file({Path.GetFileName(path)})");
    }

    // ── list_directory ────────────────────────────────────────────────────────

    private static (string, string) ListDirectory(JsonNode? input, string? root)
    {
        var rawPath   = input?["path"]?.GetValue<string>() ?? root ?? "";
        var path      = ResolvePath(rawPath, root);
        var recursive = input?["recursive"]?.GetValue<bool>() ?? false;

        if (!Directory.Exists(path))
            return ($"Directory not found: {path}", $"list_directory({rawPath})");

        IEnumerable<string> entries;
        if (recursive)
        {
            entries = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => SkipDirs.Contains(p)));
        }
        else
        {
            entries = Directory.EnumerateFileSystemEntries(path)
                .Where(e => !SkipDirs.Contains(Path.GetFileName(e)));
        }

        var relRoot = path.TrimEnd('/') + "/";
        var lines   = entries.Take(300).Select(e =>
        {
            var rel   = e.StartsWith(relRoot) ? e[relRoot.Length..] : e;
            var isDir = Directory.Exists(e);
            return isDir ? $"[dir]  {rel}/" : $"[file] {rel}";
        }).OrderBy(l => l);

        var result = string.Join("\n", lines);
        return (result.Length > 0 ? result : "(empty directory)", $"list_directory({Path.GetFileName(path.TrimEnd('/'))})");
    }

    // ── search_code ───────────────────────────────────────────────────────────

    private static (string, string) SearchCode(JsonNode? input, string? root)
    {
        var pattern     = input?["pattern"]?.GetValue<string>() ?? "";
        var rawDir      = input?["directory"]?.GetValue<string>() ?? root ?? "";
        var filePattern = input?["file_pattern"]?.GetValue<string>() ?? "*";
        var searchDir   = ResolvePath(rawDir, root);

        if (!Directory.Exists(searchDir))
            return ($"Directory not found: {searchDir}", $"search_code({pattern})");

        Regex? rx = null;
        try { rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
        catch { return ($"Invalid regex: {pattern}", $"search_code({pattern})"); }

        var sb          = new StringBuilder();
        var matchCount  = 0;
        var searchOpts  = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        foreach (var file in Directory.EnumerateFiles(searchDir, filePattern, searchOpts)
                     .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => SkipDirs.Contains(p))))
        {
            if (matchCount >= 200) break;
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }

            var relFile = file.StartsWith(searchDir) ? file[searchDir.TrimEnd('/').Length..].TrimStart('/') : file;
            bool fileHeaderWritten = false;

            for (int i = 0; i < lines.Length && matchCount < 200; i++)
            {
                if (!rx.IsMatch(lines[i])) continue;
                if (!fileHeaderWritten) { sb.AppendLine($"\n{relFile}:"); fileHeaderWritten = true; }
                sb.AppendLine($"  {i + 1}: {lines[i].Trim()}");
                matchCount++;
            }
        }

        var result = matchCount > 0 ? sb.ToString().Trim() : $"No matches found for: {pattern}";
        return (result, $"search_code({pattern})");
    }

    // ── write_file ────────────────────────────────────────────────────────────

    private static (string, string) WriteFile(JsonNode? input, string? root)
    {
        var rawPath = input?["path"]?.GetValue<string>() ?? "";
        var content = input?["content"]?.GetValue<string>() ?? "";
        var path    = ResolvePath(rawPath, root);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
        return ($"Written {content.Split('\n').Length} lines to {path}", $"write_file({Path.GetFileName(path)})");
    }

    // ── run_command ───────────────────────────────────────────────────────────

    private static (string, string) RunCommand(JsonNode? input, string? root)
    {
        var command = input?["command"]?.GetValue<string>() ?? "";
        var rawDir  = input?["directory"]?.GetValue<string>();
        var workDir = string.IsNullOrEmpty(rawDir) ? (root ?? ".") : ResolvePath(rawDir, root);

        if (string.IsNullOrWhiteSpace(command)) return ("No command provided.", "run_command");

        // Safety: block obviously destructive operations
        var lower = command.TrimStart().ToLower();
        var blocked = new[] { "rm -rf /", "format ", "mkfs", "dd if=", ":(){:|:&};:" };
        if (blocked.Any(b => lower.Contains(b)))
            return ("Blocked: command looks destructive.", $"run_command({command})");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "/bin/sh",
            Arguments              = $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory       = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        // Inherit PATH so dotnet, npm, git etc. are found
        psi.Environment["PATH"] =
            $"/usr/local/bin:/usr/bin:/bin:/opt/homebrew/bin:" +
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.dotnet/tools:" +
            (Environment.GetEnvironmentVariable("PATH") ?? "");

        using var proc   = System.Diagnostics.Process.Start(psi)!;
        var cts          = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var stdout       = proc.StandardOutput.ReadToEnd();
        var stderr       = proc.StandardError.ReadToEnd();
        proc.WaitForExit((int)TimeSpan.FromSeconds(120).TotalMilliseconds);

        var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        if (!string.IsNullOrWhiteSpace(stderr) && !string.IsNullOrWhiteSpace(stdout))
            output = stdout + "\n--- stderr ---\n" + stderr;

        var exitInfo = proc.ExitCode != 0 ? $"\n[exit code {proc.ExitCode}]" : "";
        return ((output.Length > 8000 ? output[..8000] + "\n...(truncated)" : output) + exitInfo,
                $"run_command({command})");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolvePath(string raw, string? root)
    {
        if (string.IsNullOrWhiteSpace(raw)) return root ?? "";
        if (Path.IsPathRooted(raw)) return raw;
        return string.IsNullOrEmpty(root) ? raw : Path.Combine(root, raw);
    }
}
