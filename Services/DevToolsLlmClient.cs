using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

/// <summary>Routes LLM calls to all supported providers.</summary>
public sealed class DevToolsLlmClient
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<DevToolsLlmClient> _log;

    private const string AnthropicApiBase = "https://api.anthropic.com/v1";

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly Dictionary<string, string> OaiBaseUrls = new()
    {
        ["openai"]     = "https://api.openai.com",
        ["groq"]       = "https://api.groq.com/openai",
        ["together"]   = "https://api.together.xyz",
        ["mistral"]    = "https://api.mistral.ai",
        ["perplexity"] = "https://api.perplexity.ai",
        ["deepseek"]   = "https://api.deepseek.com",
        ["lmstudio"]   = "http://localhost:1234",
        ["xai"]        = "https://api.x.ai",
    };

    public static readonly Dictionary<string, string> DefaultModels = new()
    {
        ["claude"]     = "claude-sonnet-4-6",
        ["claudecli"]  = "",          // uses whatever `claude` CLI is configured with
        ["openai"]     = "gpt-4o",
        ["groq"]       = "llama-3.3-70b-versatile",
        ["together"]   = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
        ["mistral"]    = "mistral-large-latest",
        ["perplexity"] = "sonar-pro",
        ["deepseek"]   = "deepseek-chat",
        ["gemini"]     = "gemini-2.0-flash",
        ["cohere"]     = "command-r-plus",
        ["ollama"]     = "llama3",
        ["lmstudio"]   = "local-model",
        ["azure"]      = "gpt-4o",
        ["xai"]        = "grok-3-latest",
        ["custom"]     = "",
    };

    public DevToolsLlmClient(IHttpClientFactory http, IConfiguration config, ILogger<DevToolsLlmClient> log)
    {
        _http   = http;
        _config = config;
        _log    = log;
    }

    public async Task<(string reply, int inputTokens, int outputTokens)> CallAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        try
        {
            var p = ws.LlmProvider.ToLower();
            return p switch
            {
                "claude"                                                     => await CallClaudeAsync(ws, systemPrompt, messages),
                "claudecli"                                                  => await CallClaudeCliAsync(ws, systemPrompt, messages),
                "ollama"                                                     => await CallOllamaAsync(ws, systemPrompt, messages),
                "gemini"                                                     => await CallGeminiAsync(ws, systemPrompt, messages),
                "cohere"                                                     => await CallCohereAsync(ws, systemPrompt, messages),
                "azure"                                                      => await CallAzureOpenAiAsync(ws, systemPrompt, messages),
                "openai" or "groq" or "together" or "mistral" or "perplexity"
                    or "deepseek" or "lmstudio" or "xai" or "custom"        => await CallOpenAiCompatibleAsync(ws, systemPrompt, messages),
                _                                                            => await CallClaudeAsync(ws, systemPrompt, messages)
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LLM call failed for provider {Provider}", ws.LlmProvider);
            return ($"Error calling LLM ({ws.LlmProvider}): {ex.Message}", 0, 0);
        }
    }

    // ── Streaming entry point ─────────────────────────────────────────────────

    /// <summary>
    /// Streams LLM tokens as they arrive. Falls back to yielding the full response
    /// as a single chunk for providers that do not support streaming.
    /// </summary>
    /// <param name="onTool">Optional callback invoked for each tool the agent calls,
    /// e.g. "read_file(DevToolsService.cs)". Use it to push a distinct UI event to the client.</param>
    public async IAsyncEnumerable<string> StreamAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages,
        Func<string, Task>? onTool,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var p = ws.LlmProvider.ToLower();

        var useAgentic = p == "claude"
                         && !string.IsNullOrEmpty(ws.GitRepoPath)
                         && Directory.Exists(ws.GitRepoPath);

        IAsyncEnumerable<string> source = p switch
        {
            "claude" when useAgentic
                       => StreamClaudeAgenticAsync(ws, systemPrompt, messages, onTool, ct),
            "claude"   => StreamClaudeAsync(ws, systemPrompt, messages, ct),
            "openai" or "groq" or "together" or "mistral" or "perplexity"
                or "deepseek" or "lmstudio" or "xai" or "custom"
                       => StreamOpenAiCompatibleAsync(ws, systemPrompt, messages, ct),
            _          => StreamViaNonStreamingAsync(ws, systemPrompt, messages, ct)
        };

        await foreach (var chunk in source.WithCancellation(ct).ConfigureAwait(false))
            yield return chunk;
    }

    // ── Agentic loop: full SSE streaming + tool execution ────────────────────

    private async IAsyncEnumerable<string> StreamClaudeAgenticAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages,
        Func<string, Task>? onTool,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = !string.IsNullOrEmpty(ws.ApiKey) ? ws.ApiKey : _config["Anthropic:ApiKey"] ?? "";
        var model  = string.IsNullOrEmpty(ws.LlmModel) ? DefaultModels["claude"] : ws.LlmModel;
        var tools  = DevToolsAgentTools.Definitions();
        var root   = ws.GitRepoPath!;

        var claudeMsgs = messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m => (object)BuildClaudeMessage(m))
            .ToList();

        const int maxIter = 10;
        string? loopError = null;

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model, max_tokens = 4096, stream = true, system = systemPrompt,
                tools, messages = claudeMsgs
            };
            var req = new HttpRequestMessage(HttpMethod.Post, $"{AnthropicApiBase}/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json")
            };

            HttpResponseMessage? resp = null;
            string? connectErr = null;
            try { resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            catch (Exception ex) { connectErr = $"Error: {ex.Message}"; }

            if (connectErr is not null) { loopError = connectErr; break; }
            if (!resp!.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                loopError = $"Anthropic error {resp.StatusCode}: {errBody}";
                break;
            }

            // ── Stream SSE, yield text tokens live, collect tool_use blocks ───
            var blocks     = new Dictionary<int, AgentBlock>();
            string? stopReason = null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null || !line.StartsWith("data: ")) continue;
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                // ParseAgentSseLine never yields — safe to call in the loop
                string? token = null;
                ParseAgentSseLine(data, blocks, ref stopReason, ref token);
                if (token is not null) yield return token;
            }

            var toolBlocks = blocks.Where(kv => kv.Value.Type == "tool_use")
                                   .OrderBy(kv => kv.Key)
                                   .Select(kv => kv.Value)
                                   .ToList();

            // ── Final turn: no tool calls → done ─────────────────────────────
            if (stopReason == "end_turn" || toolBlocks.Count == 0)
                yield break;

            // ── Tool turn: build assistant msg, execute tools, loop ───────────
            var assistantContent = blocks.OrderBy(kv => kv.Key).Select(kv =>
            {
                var b = kv.Value;
                if (b.Type == "text")
                    return (object)new { type = "text", text = b.Text.ToString() };
                var parsed = TryParseJson(b.InputJson.ToString());
                return (object)new { type = "tool_use", id = b.Id, name = b.Name, input = parsed };
            }).ToArray();

            claudeMsgs.Add(new { role = "assistant", content = assistantContent });

            var toolResults = new List<object>();
            foreach (var blk in toolBlocks)
            {
                if (ct.IsCancellationRequested) break;
                var inputNode = TryParseJson(blk.InputJson.ToString());
                var (result, label) = DevToolsAgentTools.Execute(blk.Name, inputNode, root);
                if (onTool is not null) await onTool(label).ConfigureAwait(false);
                toolResults.Add(new { type = "tool_result", tool_use_id = blk.Id, content = result });
            }

            claudeMsgs.Add(new { role = "user", content = toolResults.ToArray() });
        }

        if (loopError is not null) yield return $"\n⚠️ {loopError}";
    }

    // Parses one SSE data line in-place; sets token if there's text to stream.
    private static void ParseAgentSseLine(
        string data, Dictionary<int, AgentBlock> blocks,
        ref string? stopReason, ref string? token)
    {
        try
        {
            var node = JsonNode.Parse(data);
            switch (node?["type"]?.GetValue<string>())
            {
                case "content_block_start":
                    var idx = node["index"]?.GetValue<int>() ?? 0;
                    var cb  = node["content_block"];
                    blocks[idx] = new AgentBlock(
                        cb?["type"]?.GetValue<string>() ?? "",
                        cb?["id"]?.GetValue<string>()   ?? "",
                        cb?["name"]?.GetValue<string>() ?? "");
                    break;

                case "content_block_delta":
                    var dIdx  = node["index"]?.GetValue<int>() ?? 0;
                    var delta = node["delta"];
                    if (!blocks.TryGetValue(dIdx, out var blk)) break;
                    switch (delta?["type"]?.GetValue<string>())
                    {
                        case "text_delta":
                            var t = delta["text"]?.GetValue<string>() ?? "";
                            blk.Text.Append(t);
                            if (t.Length > 0) token = t;
                            break;
                        case "input_json_delta":
                            blk.InputJson.Append(delta["partial_json"]?.GetValue<string>() ?? "");
                            break;
                    }
                    break;

                case "message_delta":
                    stopReason = node["delta"]?["stop_reason"]?.GetValue<string>();
                    break;
            }
        }
        catch { /* malformed SSE line — skip */ }
    }

    private static JsonNode? TryParseJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new JsonObject();
        try { return JsonNode.Parse(json); } catch { return new JsonObject(); }
    }

    private sealed class AgentBlock
    {
        public string        Type      { get; }
        public string        Id        { get; }
        public string        Name      { get; }
        public StringBuilder Text      { get; } = new();
        public StringBuilder InputJson { get; } = new();
        public AgentBlock(string type, string id, string name) => (Type, Id, Name) = (type, id, name);
    }

    // ── Streaming: Claude (Anthropic SSE) ─────────────────────────────────────

    private async IAsyncEnumerable<string> StreamClaudeAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = !string.IsNullOrEmpty(ws.ApiKey) ? ws.ApiKey : _config["Anthropic:ApiKey"] ?? "";
        var model  = string.IsNullOrEmpty(ws.LlmModel) ? DefaultModels["claude"] : ws.LlmModel;
        var http   = _http.CreateClient();
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var body = new
        {
            model, max_tokens = 4096, stream = true, system = systemPrompt,
            messages = messages.Where(m => m.Role is "user" or "assistant")
                               .Select(m => BuildClaudeMessage(m)).ToList()
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"{AnthropicApiBase}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json")
        };

        // Send request — capture errors so we can yield them outside the catch
        HttpResponseMessage? resp = null;
        string? connectError = null;
        try { resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (Exception ex) { connectError = $"Error connecting to Claude: {ex.Message}"; }

        if (connectError is not null) { yield return connectError; yield break; }

        if (!resp!.IsSuccessStatusCode)
        {
            var errText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            yield return $"Anthropic error {resp.StatusCode}: {errText}";
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            // Parse outside try/catch so we can yield safely
            var chunk = ParseClaudeSseLine(data);
            if (chunk is not null) yield return chunk;
        }
    }

    private static string? ParseClaudeSseLine(string data)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(data);
            if (node?["type"]?.GetValue<string>() == "content_block_delta")
                return node?["delta"]?["text"]?.GetValue<string>();
        }
        catch { /* malformed line — ignore */ }
        return null;
    }

    // ── Streaming: OpenAI-compatible SSE ─────────────────────────────────────

    private async IAsyncEnumerable<string> StreamOpenAiCompatibleAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var p       = ws.LlmProvider.ToLower();
        var baseUrl = !string.IsNullOrEmpty(ws.LlmUrl) ? ws.LlmUrl.TrimEnd('/')
                      : OaiBaseUrls.TryGetValue(p, out var u) ? u : "https://api.openai.com";
        var model   = string.IsNullOrEmpty(ws.LlmModel)
                      ? DefaultModels.TryGetValue(p, out var m) ? m : "gpt-4o"
                      : ws.LlmModel;

        var http = _http.CreateClient();
        if (!string.IsNullOrEmpty(ws.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ws.ApiKey);

        var msgs = new List<object> { new { role = "system", content = systemPrompt } };
        msgs.AddRange(messages.Where(m => m.Role is "user" or "assistant")
                              .Select(m => (object)BuildOaiMessage(m)));

        var body = new { model, messages = msgs, max_tokens = 4096, stream = true };
        var req  = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? resp = null;
        string? connectError = null;
        try { resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (Exception ex) { connectError = $"Error connecting to {p}: {ex.Message}"; }

        if (connectError is not null) { yield return connectError; yield break; }

        if (!resp!.IsSuccessStatusCode)
        {
            var errText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            yield return $"{p} API error {resp.StatusCode}: {errText}";
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = ParseOaiSseLine(data);
            if (chunk is not null) yield return chunk;
        }
    }

    private static string? ParseOaiSseLine(string data)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(data);
            return node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
        }
        catch { return null; }
    }

    // ── Streaming fallback (non-streaming providers) ───────────────────────────

    /// <summary>
    /// For providers that don't support streaming (Gemini, Cohere, Azure, Ollama,
    /// Claude CLI) we call the normal API and yield the whole response as one chunk.
    /// </summary>
    private async IAsyncEnumerable<string> StreamViaNonStreamingAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (reply, _, _) = await CallAsync(ws, systemPrompt, messages).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(reply))
            yield return reply;
    }

    // ── Provider implementations ───────────────────────────────────────────────

    private async Task<(string, int, int)> CallClaudeAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        var apiKey = !string.IsNullOrEmpty(ws.ApiKey) ? ws.ApiKey : _config["Anthropic:ApiKey"] ?? "";
        var model  = string.IsNullOrEmpty(ws.LlmModel) ? DefaultModels["claude"] : ws.LlmModel;
        var http   = _http.CreateClient();
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var body = new
        {
            model, max_tokens = 4096, system = systemPrompt,
            messages = messages.Where(m => m.Role is "user" or "assistant")
                               .Select(m => BuildClaudeMessage(m)).ToList()
        };
        var resp = await http.PostAsync($"{AnthropicApiBase}/messages",
            new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json"));
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return ($"Anthropic error {resp.StatusCode}: {text}", 0, 0);
        var root = JsonNode.Parse(text);
        return (root?["content"]?[0]?["text"]?.GetValue<string>() ?? "",
                root?["usage"]?["input_tokens"]?.GetValue<int>() ?? 0,
                root?["usage"]?["output_tokens"]?.GetValue<int>() ?? 0);
    }

    private static object BuildClaudeMessage(DevToolsChatMessage m)
    {
        if (m.Images is { Count: > 0 } && m.Role == "user")
        {
            var parts = new List<object>();
            foreach (var img in m.Images)
            {
                var (mimeType, b64) = ParseDataUri(img);
                parts.Add(new { type = "image", source = new { type = "base64", media_type = mimeType, data = b64 } });
            }
            parts.Add(new { type = "text", text = m.Content });
            return new { role = m.Role, content = parts };
        }
        return new { role = m.Role, content = m.Content };
    }

    private async Task<(string, int, int)> CallClaudeCliAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        // Build full prompt: system context + conversation history collapsed into one input
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);
        sb.AppendLine();
        var history = messages.Where(m => m.Role is "user" or "assistant").ToList();
        foreach (var m in history.SkipLast(1))
        {
            sb.AppendLine($"[{m.Role.ToUpper()}]");
            sb.AppendLine(m.Content);
            sb.AppendLine();
        }
        var last = history.LastOrDefault(m => m.Role == "user");
        if (last is not null) sb.Append(last.Content);
        var prompt = sb.ToString();

        // Locate claude binary
        var claudePath = FindClaudePath();
        if (string.IsNullOrEmpty(claudePath))
            return ("Claude Code CLI not found. Make sure `claude` is installed and in your PATH.", 0, 0);

        // Build args
        var argParts = new List<string> { "--print" };
        if (!string.IsNullOrEmpty(ws.LlmModel))
            argParts.AddRange(new[] { "--model", ws.LlmModel });
        // Give the CLI access to the project folder so it can read files natively
        if (!string.IsNullOrEmpty(ws.GitRepoPath) && Directory.Exists(ws.GitRepoPath))
            argParts.AddRange(new[] { "--add-dir", ws.GitRepoPath });

        var psi = new ProcessStartInfo
        {
            FileName               = claudePath,
            Arguments              = string.Join(" ", argParts),
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = $"/usr/local/bin:/usr/bin:/opt/homebrew/bin:" +
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.npm-global/bin:" +
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.local/bin:{pathEnv}";
        if (!string.IsNullOrEmpty(ws.GitRepoPath) && Directory.Exists(ws.GitRepoPath))
            psi.WorkingDirectory = ws.GitRepoPath;

        using var proc = Process.Start(psi)!;
        await proc.StandardInput.WriteAsync(prompt);
        proc.StandardInput.Close();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var output = await proc.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
        await proc.WaitForExitAsync(cts.Token);

        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            return ($"Claude CLI error (exit {proc.ExitCode}): {stderr}", 0, 0);

        var reply = output.Trim();
        return (reply, EstimateTokens(prompt), EstimateTokens(reply));
    }

    private static string? FindClaudePath()
    {
        var candidates = new[]
        {
            "/usr/local/bin/claude",
            "/usr/bin/claude",
            "/opt/homebrew/bin/claude",
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.npm-global/bin/claude",
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.local/bin/claude",
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.nvm/versions/node/current/bin/claude",
        };
        foreach (var p in candidates)
            if (File.Exists(p)) return p;
        // Try which
        try
        {
            var r = Process.Start(new ProcessStartInfo("which", "claude")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var path = r?.StandardOutput.ReadToEnd().Trim();
            r?.WaitForExit(3000);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        }
        catch { }
        return null;
    }

    private async Task<(string, int, int)> CallOllamaAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        var baseUrl = string.IsNullOrEmpty(ws.LlmUrl) ? "http://localhost:11434" : ws.LlmUrl.TrimEnd('/');
        var model   = string.IsNullOrEmpty(ws.LlmModel) ? DefaultModels["ollama"] : ws.LlmModel;
        var http    = _http.CreateClient();
        var msgs    = new List<object> { new { role = "system", content = systemPrompt } };
        msgs.AddRange(messages.Where(m => m.Role is "user" or "assistant")
                              .Select(m => (object)new { role = m.Role, content = m.Content }));
        var resp  = await http.PostAsync($"{baseUrl}/api/chat",
            new StringContent(JsonSerializer.Serialize(new { model, messages = msgs, stream = false }), Encoding.UTF8, "application/json"));
        var text  = await resp.Content.ReadAsStringAsync();
        var reply = JsonNode.Parse(text)?["message"]?["content"]?.GetValue<string>() ?? "";
        var est   = EstimateTokens(reply);
        return (reply, est, est);
    }

    private async Task<(string, int, int)> CallOpenAiCompatibleAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        var p       = ws.LlmProvider.ToLower();
        var baseUrl = !string.IsNullOrEmpty(ws.LlmUrl) ? ws.LlmUrl.TrimEnd('/')
                      : OaiBaseUrls.TryGetValue(p, out var u) ? u : "https://api.openai.com";
        var model   = string.IsNullOrEmpty(ws.LlmModel)
                      ? DefaultModels.TryGetValue(p, out var m) ? m : "gpt-4o"
                      : ws.LlmModel;
        var http    = _http.CreateClient();
        if (!string.IsNullOrEmpty(ws.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ws.ApiKey);
        var msgs = new List<object> { new { role = "system", content = systemPrompt } };
        msgs.AddRange(messages.Where(m => m.Role is "user" or "assistant")
                              .Select(m => (object)BuildOaiMessage(m)));
        var resp = await http.PostAsync($"{baseUrl}/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(new { model, messages = msgs, max_tokens = 4096 }), Encoding.UTF8, "application/json"));
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return ($"{p} API error {resp.StatusCode}: {text}", 0, 0);
        var root = JsonNode.Parse(text);
        return (root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "",
                root?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
                root?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0);
    }

    private async Task<(string, int, int)> CallGeminiAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        var apiKey = ws.ApiKey ?? "";
        var model  = string.IsNullOrEmpty(ws.LlmModel) ? DefaultModels["gemini"] : ws.LlmModel;
        var http   = _http.CreateClient();
        var geminiMessages = messages.Where(m => m.Role is "user" or "assistant").Select(m =>
        {
            var role  = m.Role == "assistant" ? "model" : "user";
            if (m.Images is { Count: > 0 } && m.Role == "user")
            {
                var parts = new List<object>();
                foreach (var img in m.Images)
                {
                    var (mimeType, b64) = ParseDataUri(img);
                    parts.Add(new { inline_data = new { mime_type = mimeType, data = b64 } });
                }
                parts.Add(new { text = m.Content });
                return new { role, parts = (object)parts };
            }
            return new { role, parts = (object)new[] { new { text = m.Content } } };
        }).ToList();
        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents           = geminiMessages,
            generationConfig   = new { maxOutputTokens = 4096 }
        };
        var url  = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        var resp = await http.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return ($"Gemini error {resp.StatusCode}: {text}", 0, 0);
        var root   = JsonNode.Parse(text);
        var reply  = root?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";
        var inTok  = root?["usageMetadata"]?["promptTokenCount"]?.GetValue<int>() ?? 0;
        var outTok = root?["usageMetadata"]?["candidatesTokenCount"]?.GetValue<int>() ?? 0;
        return (reply, inTok, outTok);
    }

    private async Task<(string, int, int)> CallCohereAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        var apiKey = ws.ApiKey ?? "";
        var model  = string.IsNullOrEmpty(ws.LlmModel) ? DefaultModels["cohere"] : ws.LlmModel;
        var http   = _http.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var chatHistory = messages.SkipLast(1).Where(m => m.Role is "user" or "assistant").Select(m => new
        {
            role    = m.Role == "assistant" ? "CHATBOT" : "USER",
            message = m.Content
        }).ToList();
        var last = messages.LastOrDefault(m => m.Role == "user");
        var body = new { model, preamble = systemPrompt, chat_history = chatHistory, message = last?.Content ?? "" };
        var resp = await http.PostAsync("https://api.cohere.ai/v1/chat",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        var text   = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return ($"Cohere error {resp.StatusCode}: {text}", 0, 0);
        var root   = JsonNode.Parse(text);
        var reply  = root?["text"]?.GetValue<string>() ?? "";
        var inTok  = root?["meta"]?["billed_units"]?["input_tokens"]?.GetValue<int>() ?? EstimateTokens(reply);
        var outTok = root?["meta"]?["billed_units"]?["output_tokens"]?.GetValue<int>() ?? EstimateTokens(reply);
        return (reply, inTok, outTok);
    }

    private async Task<(string, int, int)> CallAzureOpenAiAsync(
        DevToolsWorkspace ws, string systemPrompt, List<DevToolsChatMessage> messages)
    {
        if (string.IsNullOrEmpty(ws.LlmUrl)) return ("Azure OpenAI: set Base URL to your deployment endpoint.", 0, 0);
        var http = _http.CreateClient();
        http.DefaultRequestHeaders.Add("api-key", ws.ApiKey ?? "");
        var msgs = new List<object> { new { role = "system", content = systemPrompt } };
        msgs.AddRange(messages.Where(m => m.Role is "user" or "assistant")
                              .Select(m => (object)new { role = m.Role, content = m.Content }));
        var url  = $"{ws.LlmUrl.TrimEnd('/')}/chat/completions?api-version=2024-02-01";
        var resp = await http.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(new { messages = msgs, max_tokens = 4096 }), Encoding.UTF8, "application/json"));
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return ($"Azure OpenAI error {resp.StatusCode}: {text}", 0, 0);
        var root = JsonNode.Parse(text);
        return (root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "",
                root?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
                root?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0);
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    public static string BuildSystemPrompt(DevToolsWorkspace ws, DevToolsSession session,
        DevToolsChatRequest req, List<DevToolsSkill> enabledSkills)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous software engineering agent in the Skanyxx Dev Tools workspace.");

        var hasTools = !string.IsNullOrEmpty(ws.GitRepoPath) && Directory.Exists(ws.GitRepoPath)
                       && ws.LlmProvider.Equals("claude", StringComparison.OrdinalIgnoreCase);
        if (hasTools)
        {
            sb.AppendLine($"\nProject root: {ws.GitRepoPath}");
            sb.AppendLine("""

CRITICAL BEHAVIOUR RULES — follow these without exception:
1. NEVER ask the user to paste code, share files, or run commands themselves.
2. Use read_file / list_directory / search_code to find and read whatever you need autonomously.
3. Use write_file to create or modify files directly — do not show the user code and ask them to create it.
4. Use run_command to execute dotnet, git, npm, or any shell command — do not show instructions.
5. When a task requires multiple steps, do all of them with tool calls, then report what you did.
6. Only respond with text when you have completed the work or need a genuine decision from the user.
""");
        }

        if (!string.IsNullOrEmpty(req.TaskTitle))
        {
            sb.AppendLine($"\nCurrent task: [{req.TaskId}] {req.TaskTitle}");
            if (!string.IsNullOrEmpty(req.TaskDescription))
                sb.AppendLine($"Description: {req.TaskDescription}");
        }
        if (!string.IsNullOrEmpty(session.SummaryContext))
            sb.AppendLine($"\nPrevious conversation summary:\n{session.SummaryContext}");
        foreach (var skill in enabledSkills)
            sb.AppendLine($"\n{skill.SystemPromptAddition}");

        if (!hasTools)
            sb.AppendLine("\nProvide concise, actionable responses. Use code blocks with language identifiers.");

        return sb.ToString();
    }

    public static int EstimateTokens(List<DevToolsChatMessage> messages) =>
        messages.Sum(m => EstimateTokens(m.Content));

    public static int EstimateTokens(string text) => text.Length / 4;

    private static object BuildOaiMessage(DevToolsChatMessage m)
    {
        if (m.Images is { Count: > 0 } && m.Role == "user")
        {
            var parts = new List<object>();
            parts.Add(new { type = "text", text = m.Content });
            foreach (var img in m.Images)
                parts.Add(new { type = "image_url", image_url = new { url = img } });
            return new { role = m.Role, content = parts };
        }
        return new { role = m.Role, content = m.Content };
    }

    /// <summary>Splits "data:image/png;base64,XXXX" into ("image/png", "XXXX").</summary>
    private static (string mimeType, string data) ParseDataUri(string dataUri)
    {
        var semi  = dataUri.IndexOf(';');
        var comma = dataUri.IndexOf(',');
        var mime  = semi > 0 ? dataUri[5..semi] : "image/png";
        var data  = comma > 0 ? dataUri[(comma + 1)..] : dataUri;
        return (mime, data);
    }
}
