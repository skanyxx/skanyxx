using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// The slice of Gitea 1.24's API the studio uses (research/gitea-api.md), in memory on real Kestrel: one org, one repo,
/// branches as file maps, pull requests, the main protection rule (no direct writes), merges pinned to a head commit.
/// Every request must carry <see cref="Token"/>; requests are recorded. Knobs: <see cref="Down"/>, <see cref="MoveHeadOnMerge"/>,
/// and <see cref="PushToBranch"/> to put files on a branch the way someone with git access would.
/// </summary>
public sealed class FakeGitea : IAsyncDisposable
{
    public const string Token = "gitea-token-NEVER-SHOWN-0123456789";

    private readonly WebApplication _app;
    private readonly object _lock = new();
    private int _commits;

    private FakeGitea(WebApplication app) => _app = app;

    public sealed record Call(string Method, string Path, JsonNode? Body);

    public sealed class Pull
    {
        public required int Number { get; init; }
        public required string Head { get; init; }
        public required string Title { get; init; }
        public required string Body { get; init; }
        public string Base { get; init; } = "main";
        public bool Open { get; set; } = true;
        public bool Merged { get; set; }
    }

    public ConcurrentQueue<Call> Calls { get; } = new();

    public HashSet<string> Orgs { get; } = [];

    /// <summary>Repo full name → branch → (path → content); every branch also has a head commit id.</summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Repos { get; } = [];

    public Dictionary<string, string> Heads { get; } = [];

    public List<JsonObject> Protections { get; } = [];

    /// <summary>Repo full name → git's id and creation time (D119): a re-created repo gets new ones.</summary>
    public Dictionary<string, (long Id, string CreatedAt)> RepoMeta { get; } = [];

    private long _repoIds;

    public bool PublicRepo { get; set; }

    /// <summary>Answered for the tree of main instead of the real one (a shape nobody expects).</summary>
    public string? TreeBody { get; set; }

    /// <summary>The merge happens, but its answer is lost (a 502 from a proxy).</summary>
    public bool LoseMergeAnswer { get; set; }

    /// <summary>Runs once git has merged, before it answers (a person closing the page right then).</summary>
    public Action? OnMerge { get; set; }

    public List<Pull> Pulls { get; } = [];

    public bool Down { get; set; }

    public bool MoveHeadOnMerge { get; set; }

    public int Port => new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public Dictionary<string, string> Main(string repo = "skanyxx/skanyxx-agents") => Repos[repo]["main"];

    public static async Task<FakeGitea> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var fake = new FakeGitea(app);
        fake.Map(app);
        await app.StartAsync();
        return fake;
    }

    /// <summary>Writes files on a branch (creating it from main), as a push by someone with git access would.</summary>
    public void PushToBranch(string branch, Dictionary<string, string?> files, string repo = "skanyxx/skanyxx-agents")
    {
        lock (_lock)
        {
            var branches = Repos[repo];
            if (!branches.ContainsKey(branch))
                branches[branch] = new Dictionary<string, string>(branches["main"]);
            foreach (var (path, content) in files)
                if (content is null)
                    branches[branch].Remove(path);
                else
                    branches[branch][path] = content;
            Heads[$"{repo}:{branch}"] = NewCommit();
        }
    }

    /// <summary>A pull request from <paramref name="branch"/>, opened by someone else than Skanyxx.</summary>
    public int OpenPull(string branch, string title, string body, string @base = "main")
    {
        lock (_lock)
        {
            var pull = new Pull { Number = Pulls.Count + 1, Head = branch, Title = title, Body = body, Base = @base };
            Pulls.Add(pull);
            return pull.Number;
        }
    }

    /// <summary>The repo is gone, as after a lost git volume: files, pull requests, protection, identity.</summary>
    public void WipeRepo(string repo = "skanyxx/skanyxx-agents")
    {
        lock (_lock)
        {
            Repos.Remove(repo);
            RepoMeta.Remove(repo);
            Protections.Clear();
            Pulls.Clear();
        }
    }

    /// <summary>Someone made a new, empty repo under the same name (a README on main, protected like the studio's).</summary>
    public void RecreateRepo(string repo = "skanyxx/skanyxx-agents")
    {
        lock (_lock)
        {
            var protections = Protections.Select(p => (JsonObject)p.DeepClone()).ToList();
            WipeRepo(repo);
            CreateRepo(repo);
            Protections.AddRange(protections);
        }
    }

    private void CreateRepo(string name)
    {
        Repos[name] = new() { ["main"] = new() { ["README.md"] = "# skanyxx-agents\n" } };
        Heads[$"{name}:main"] = NewCommit();
        RepoMeta[name] = (++_repoIds, DateTime.UtcNow.AddSeconds(_repoIds).ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    private string NewCommit() => (++_commits).ToString("x40");

    private void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            JsonNode? body = null;
            if (context.Request.ContentLength > 0)
            {
                body = await JsonNode.ParseAsync(context.Request.Body);
                context.Request.Body.Position = 0;
            }
            Calls.Enqueue(new Call(context.Request.Method, context.Request.Path + context.Request.QueryString, body));
            if (Down)
            {
                context.Response.StatusCode = 503;
                return;
            }
            if (context.Request.Headers.Authorization.ToString() != $"token {Token}")
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Only signed in user is allowed to call APIs." });
                return;
            }
            await next();
        });

        app.MapGet("/api/v1/orgs/{org}", (string org) => Orgs.Contains(org) ? Results.Json(new { username = org }) : NotFound());
        app.MapPost("/api/v1/orgs", (JsonObject body) =>
            Orgs.Add(body["username"]!.GetValue<string>()) ? Results.Json(body, statusCode: 201) : Results.Json(new { message = "user already exists" }, statusCode: 422));
        app.MapGet("/api/v1/repos/{owner}/{repo}", (string owner, string repo) =>
            RepoMeta.TryGetValue($"{owner}/{repo}", out var meta)
                ? Results.Json(new JsonObject
                {
                    ["id"] = meta.Id, ["full_name"] = $"{owner}/{repo}", ["created_at"] = meta.CreatedAt, ["private"] = !PublicRepo, ["default_branch"] = "main"
                })
                : NotFound());
        app.MapPost("/api/v1/orgs/{org}/repos", (string org, JsonObject body) =>
        {
            lock (_lock)
            {
                var name = $"{org}/{body["name"]!.GetValue<string>()}";
                if (!Orgs.Contains(org))
                    return NotFound();
                if (Repos.ContainsKey(name))
                    return Results.Json(new { message = "repository already exists" }, statusCode: 409);
                CreateRepo(name);
                return Results.Json(new { full_name = name, private_ = true }, statusCode: 201);
            }
        });
        app.MapGet("/api/v1/repos/{owner}/{repo}/branch_protections", () => Results.Json(new JsonArray([.. Protections.Select(p => (JsonNode)p.DeepClone())])));
        app.MapPost("/api/v1/repos/{owner}/{repo}/branch_protections", (JsonObject body) =>
        {
            if (Protections.Any(p => p["rule_name"]?.GetValue<string>() == body["rule_name"]?.GetValue<string>()))
                return Results.Json(new { message = "Branch protection already exist" }, statusCode: 403);
            Protections.Add(body);
            return Results.Json(body, statusCode: 201);
        });

        app.MapPost("/api/v1/repos/{owner}/{repo}/contents", (string owner, string repo, JsonObject body) =>
        {
            lock (_lock)
            {
                var name = $"{owner}/{repo}";
                var branches = Repos[name];
                var from = body["branch"]!.GetValue<string>();
                var target = body["new_branch"]?.GetValue<string>() ?? from;
                if (target == "main" && Protections.Any(p => p["rule_name"]?.GetValue<string>() == "main" && p["enable_push"]?.GetValue<bool>() == false))
                    return Results.Json(new { message = "user cannot commit to repo" }, statusCode: 403);
                if (body["new_branch"] is not null && branches.ContainsKey(target))
                    return Results.Json(new { message = "branch already exists" }, statusCode: 422);
                var files = new Dictionary<string, string>(branches[from]);
                foreach (var file in body["files"]!.AsArray())
                {
                    var path = file!["path"]!.GetValue<string>();
                    var operation = file["operation"]!.GetValue<string>();
                    if (operation == "create" && files.ContainsKey(path))
                        return Results.Json(new { message = "repository file already exists" }, statusCode: 422);
                    if (operation == "delete")
                        files.Remove(path);
                    else
                        files[path] = Encoding.UTF8.GetString(Convert.FromBase64String(file["content"]!.GetValue<string>()));
                }
                branches[target] = files;
                Heads[$"{name}:{target}"] = NewCommit();
                return Results.Json(new { commit = new { sha = Heads[$"{name}:{target}"] } }, statusCode: 201);
            }
        });

        app.MapPost("/api/v1/repos/{owner}/{repo}/pulls", (string owner, string repo, JsonObject body) =>
        {
            lock (_lock)
            {
                var head = body["head"]!.GetValue<string>();
                if (Pulls.Any(p => p.Open && p.Head == head))
                    return Results.Json(new { message = "pull request already exists" }, statusCode: 409);
                var pull = new Pull { Number = Pulls.Count + 1, Head = head, Title = body["title"]!.GetValue<string>(), Body = body["body"]?.GetValue<string>() ?? "" };
                Pulls.Add(pull);
                return Results.Json(PullJson($"{owner}/{repo}", pull), statusCode: 201);
            }
        });
        app.MapGet("/api/v1/repos/{owner}/{repo}/pulls", (string owner, string repo, int? page, int? limit) =>
            Results.Json(new JsonArray([.. Pulls.Where(p => p.Open).Skip(((page ?? 1) - 1) * (limit ?? 50)).Take(limit ?? 50)
                .Select(p => (JsonNode)PullJson($"{owner}/{repo}", p))])));
        app.MapGet("/api/v1/repos/{owner}/{repo}/pulls/{number:int}", (string owner, string repo, int number) =>
            Pulls.FirstOrDefault(p => p.Number == number) is { } pull ? Results.Json(PullJson($"{owner}/{repo}", pull)) : NotFound());
        app.MapGet("/api/v1/repos/{owner}/{repo}/pulls/{number:int}/files", (string owner, string repo, int number) =>
        {
            var pull = Pulls.First(p => p.Number == number);
            var branches = Repos[$"{owner}/{repo}"];
            var head = branches[pull.Head];
            var main = branches["main"];
            var changes = head.Where(f => !main.TryGetValue(f.Key, out var old) || old != f.Value)
                .Select(f => (JsonNode)new JsonObject { ["filename"] = f.Key, ["status"] = main.ContainsKey(f.Key) ? "modified" : "added" })
                .Concat(main.Keys.Where(k => !head.ContainsKey(k)).Select(k => (JsonNode)new JsonObject { ["filename"] = k, ["status"] = "deleted" }));
            return Results.Json(new JsonArray([.. changes]));
        });
        app.MapPost("/api/v1/repos/{owner}/{repo}/pulls/{number:int}/merge", (string owner, string repo, int number, JsonObject body) =>
        {
            lock (_lock)
            {
                var name = $"{owner}/{repo}";
                var pull = Pulls.First(p => p.Number == number);
                if (!pull.Open)
                    return Results.Json(new { message = "" }, statusCode: 405);
                if (MoveHeadOnMerge)
                    Heads[$"{name}:{pull.Head}"] = NewCommit();
                if (body["head_commit_id"]?.GetValue<string>() != Heads[$"{name}:{pull.Head}"])
                    return Results.Json(new { message = "head out of date" }, statusCode: 409);
                var branches = Repos[name];
                branches[pull.Base] = new Dictionary<string, string>(branches[pull.Head]);
                Heads[$"{name}:{pull.Base}"] = NewCommit();
                pull.Open = false;
                pull.Merged = true;
                if (body["delete_branch_after_merge"]?.GetValue<bool>() == true)
                    branches.Remove(pull.Head);
                OnMerge?.Invoke();
                return LoseMergeAnswer ? Results.StatusCode(502) : Results.Ok();
            }
        });
        app.MapMethods("/api/v1/repos/{owner}/{repo}/pulls/{number:int}", ["PATCH"], (string owner, string repo, int number, JsonObject body) =>
        {
            var pull = Pulls.First(p => p.Number == number);
            if (body["state"]?.GetValue<string>() == "closed")
                pull.Open = false;
            return Results.Json(PullJson($"{owner}/{repo}", pull), statusCode: 201);
        });
        app.MapDelete("/api/v1/repos/{owner}/{repo}/branches/{**branch}", (string owner, string repo, string branch) =>
            Repos[$"{owner}/{repo}"].Remove(Uri.UnescapeDataString(branch)) ? Results.NoContent() : NotFound());

        app.MapGet("/api/v1/repos/{owner}/{repo}/raw/{**path}", (string owner, string repo, string path, string @ref) =>
        {
            var name = $"{owner}/{repo}";
            var branch = Repos[name].ContainsKey(@ref) ? @ref : Heads.FirstOrDefault(h => h.Key.StartsWith(name + ":") && h.Value == @ref).Key?.Split(':')[1];
            return branch is not null && Repos[name].TryGetValue(branch, out var files) && files.TryGetValue(Uri.UnescapeDataString(path), out var content)
                ? Results.Text(content)
                : NotFound();
        });
        // Paged like Gitea: per_page entries from page, truncated while more follow.
        app.MapGet("/api/v1/repos/{owner}/{repo}/git/trees/{branch}", (string owner, string repo, string branch, int? page, int? per_page) =>
        {
            if (TreeBody is { } raw)
                return Results.Content(raw, "application/json");
            var keys = Repos[$"{owner}/{repo}"][branch].Keys.Order(StringComparer.Ordinal).ToList();
            var (from, size) = (((page ?? 1) - 1) * (per_page ?? 1000), per_page ?? 1000);
            return Results.Json(new JsonObject
            {
                ["sha"] = Heads[$"{owner}/{repo}:{branch}"],
                ["page"] = page ?? 1,
                ["total_count"] = keys.Count,
                ["truncated"] = from + size < keys.Count,
                ["tree"] = new JsonArray([.. keys.Skip(from).Take(size).Select(k => (JsonNode)new JsonObject { ["path"] = k, ["type"] = "blob" })])
            });
        });
    }

    private JsonObject PullJson(string repo, Pull pull) => new()
    {
        ["number"] = pull.Number,
        ["title"] = pull.Title,
        ["body"] = pull.Body,
        ["state"] = pull.Open ? "open" : "closed",
        ["merged"] = pull.Merged,
        ["mergeable"] = pull.Open,
        ["base"] = new JsonObject { ["ref"] = pull.Base },
        ["head"] = new JsonObject { ["ref"] = pull.Head, ["sha"] = Heads.GetValueOrDefault($"{repo}:{pull.Head}") ?? "" }
    };

    private static IResult NotFound() => Results.Json(new { message = "The target couldn't be found." }, statusCode: 404);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
