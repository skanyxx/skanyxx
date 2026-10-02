using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

/// <summary>Fetches tasks from all 12 supported task-management sources.</summary>
internal sealed class DevToolsTaskFetcher
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger _log;

    public DevToolsTaskFetcher(IHttpClientFactory http, ILogger log)
    {
        _http = http;
        _log  = log;
    }

    public Task<List<DevToolsTaskItem>> FetchAsync(string source, DevToolsWorkspace ws, DevToolsAdminGlobalDto g) =>
        source.ToLower() switch
        {
            "jira"        => FetchJiraAsync(ws, g),
            "monday"      => FetchMondayAsync(ws, g),
            "linear"      => FetchLinearAsync(g),
            "github"      => FetchGitHubAsync(g),
            "gitlab"      => FetchGitLabAsync(g),
            "azuredevops" => FetchAzureDevOpsAsync(g),
            "asana"       => FetchAsanaAsync(g),
            "clickup"     => FetchClickUpAsync(g),
            "youtrack"    => FetchYouTrackAsync(g),
            "trello"      => FetchTrelloAsync(g),
            "shortcut"    => FetchShortcutAsync(g),
            "notion"      => FetchNotionAsync(g),
            _             => Task.FromResult(new List<DevToolsTaskItem>())
        };

    private async Task<List<DevToolsTaskItem>> FetchJiraAsync(DevToolsWorkspace ws, DevToolsAdminGlobalDto g)
    {
        var url   = Coalesce(ws.JiraUrl, g.JiraUrl);
        var email = Coalesce(ws.JiraEmail, g.JiraEmail);
        var token = Coalesce(ws.JiraToken, g.JiraToken);
        var proj  = Coalesce(ws.JiraProjectKey, g.JiraProjectKey);
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}")));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var filter = string.IsNullOrEmpty(proj) ? "" : $"project={proj} AND ";
            var jql  = Uri.EscapeDataString($"{filter}statusCategory != Done ORDER BY created DESC");
            var resp = await http.GetStringAsync(
                $"{url.TrimEnd('/')}/rest/api/3/search?jql={jql}&maxResults=50&fields=summary,description,status,issuetype,priority");
            var root = JsonNode.Parse(resp);
            return (root?["issues"]?.AsArray() ?? new()).Select(i => new DevToolsTaskItem
            {
                Id          = i?["key"]?.GetValue<string>() ?? "",
                Title       = i?["fields"]?["summary"]?.GetValue<string>() ?? "",
                Description = ExtractJiraAdf(i?["fields"]?["description"]),
                Status      = i?["fields"]?["status"]?["name"]?.GetValue<string>() ?? "",
                Type        = i?["fields"]?["issuetype"]?["name"]?.GetValue<string>() ?? "task",
                Priority    = i?["fields"]?["priority"]?["name"]?.GetValue<string>() ?? "",
                Url         = $"{url.TrimEnd('/')}/browse/{i?["key"]?.GetValue<string>()}",
                Source      = "jira"
            }).ToList();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Jira fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchMondayAsync(DevToolsWorkspace ws, DevToolsAdminGlobalDto g)
    {
        var token   = Coalesce(ws.MondayToken, g.MondayToken);
        var boardId = Coalesce(ws.MondayBoardId, g.MondayBoardId);
        if (string.IsNullOrEmpty(token)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Add("Authorization", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var bf   = string.IsNullOrEmpty(boardId) ? "" : $"(ids: [{boardId}])";
            var body = new StringContent(
                $$$"""{"query": "{ boards{{{bf}}} { items_page(limit:50) { items { id name column_values { id text } } } } }"}""",
                Encoding.UTF8, "application/json");
            var json  = await (await http.PostAsync("https://api.monday.com/v2", body)).Content.ReadAsStringAsync();
            var items = new List<DevToolsTaskItem>();
            foreach (var board in JsonNode.Parse(json)?["data"]?["boards"]?.AsArray() ?? new())
            foreach (var item  in board?["items_page"]?["items"]?.AsArray() ?? new())
            {
                var cols = item?["column_values"]?.AsArray() ?? new();
                items.Add(new DevToolsTaskItem
                {
                    Id     = item?["id"]?.GetValue<string>() ?? "",
                    Title  = item?["name"]?.GetValue<string>() ?? "",
                    Status = cols.FirstOrDefault(c => c?["id"]?.GetValue<string>() == "status")?["text"]?.GetValue<string>() ?? "",
                    Type   = "task", Source = "monday"
                });
            }
            return items;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Monday fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchLinearAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.LinearToken)) return new();
        try
        {
            var http       = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", g.LinearToken);
            var teamFilter = string.IsNullOrEmpty(g.LinearTeamKey) ? ""
                : $", filter: {{ team: {{ key: {{ eq: \"{g.LinearTeamKey}\" }} }} }}";
            var query = $$"""{"query": "{ issues(first: 50{{teamFilter}}, filter: { completedAt: { null: true } }) { nodes { id identifier title description state { name } priority } } }"}""";
            var json  = await (await http.PostAsync("https://api.linear.app/graphql",
                new StringContent(query, Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync();
            return JsonNode.Parse(json)?["data"]?["issues"]?["nodes"]?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = i?["identifier"]?.GetValue<string>() ?? "",
                Title       = i?["title"]?.GetValue<string>() ?? "",
                Description = i?["description"]?.GetValue<string>() ?? "",
                Status      = i?["state"]?["name"]?.GetValue<string>() ?? "",
                Priority    = LinearPriority(i?["priority"]?.GetValue<int>() ?? 0),
                Type        = "issue", Source = "linear"
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Linear fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchGitHubAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.GitHubOwner) || string.IsNullOrEmpty(g.GitHubRepo)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Add("User-Agent", "Skanyxx-DevTools");
            if (!string.IsNullOrEmpty(g.GitHubToken))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", g.GitHubToken);
            var json = await http.GetStringAsync(
                $"https://api.github.com/repos/{g.GitHubOwner}/{g.GitHubRepo}/issues?state=open&per_page=50");
            return JsonNode.Parse(json)?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = $"#{i?["number"]?.GetValue<int>()}",
                Title       = i?["title"]?.GetValue<string>() ?? "",
                Description = i?["body"]?.GetValue<string>() ?? "",
                Status      = i?["state"]?.GetValue<string>() ?? "open",
                Type        = i?["pull_request"] != null ? "pr" : "issue",
                Url         = i?["html_url"]?.GetValue<string>(),
                Source      = "github"
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "GitHub fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchGitLabAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.GitLabToken) || string.IsNullOrEmpty(g.GitLabProjectId)) return new();
        try
        {
            var baseUrl = string.IsNullOrEmpty(g.GitLabUrl) ? "https://gitlab.com" : g.GitLabUrl.TrimEnd('/');
            var http    = _http.CreateClient();
            http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", g.GitLabToken);
            var json = await http.GetStringAsync(
                $"{baseUrl}/api/v4/projects/{Uri.EscapeDataString(g.GitLabProjectId)}/issues?state=opened&per_page=50");
            return JsonNode.Parse(json)?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = $"#{i?["iid"]?.GetValue<int>()}",
                Title       = i?["title"]?.GetValue<string>() ?? "",
                Description = i?["description"]?.GetValue<string>() ?? "",
                Status      = i?["state"]?.GetValue<string>() ?? "opened",
                Type        = i?["type"]?.GetValue<string>()?.ToLower() ?? "issue",
                Priority    = i?["priority"]?.GetValue<string>() ?? "",
                Url         = i?["web_url"]?.GetValue<string>(),
                Source      = "gitlab"
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "GitLab fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchAzureDevOpsAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.AzureDevOpsOrg) || string.IsNullOrEmpty(g.AzureDevOpsPat)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($":{g.AzureDevOpsPat}")));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var proj    = g.AzureDevOpsProject ?? "";
            var wiqlUrl = $"https://dev.azure.com/{g.AzureDevOpsOrg}/{proj}/_apis/wit/wiql?api-version=7.0";
            var wiql    = new StringContent(
                """{"query": "SELECT [System.Id],[System.Title],[System.State],[System.WorkItemType],[Microsoft.VSTS.Common.Priority] FROM WorkItems WHERE [System.State] <> 'Closed' ORDER BY [System.ChangedDate] DESC"}""",
                Encoding.UTF8, "application/json");
            var wiqlJson = await (await http.PostAsync(wiqlUrl, wiql)).Content.ReadAsStringAsync();
            var ids = JsonNode.Parse(wiqlJson)?["workItems"]?.AsArray()
                .Take(50).Select(w => w?["id"]?.GetValue<int>().ToString()).ToList() ?? new();
            if (!ids.Any()) return new();
            var batchUrl  = $"https://dev.azure.com/{g.AzureDevOpsOrg}/{proj}/_apis/wit/workitems?ids={string.Join(",", ids)}&fields=System.Id,System.Title,System.State,System.WorkItemType,Microsoft.VSTS.Common.Priority&api-version=7.0";
            var batchJson = await http.GetStringAsync(batchUrl);
            return JsonNode.Parse(batchJson)?["value"]?.AsArray().Select(i =>
            {
                var f = i?["fields"];
                return new DevToolsTaskItem
                {
                    Id       = i?["id"]?.GetValue<int>().ToString() ?? "",
                    Title    = f?["System.Title"]?.GetValue<string>() ?? "",
                    Status   = f?["System.State"]?.GetValue<string>() ?? "",
                    Type     = f?["System.WorkItemType"]?.GetValue<string>()?.ToLower() ?? "task",
                    Priority = f?["Microsoft.VSTS.Common.Priority"]?.GetValue<int>().ToString() ?? "",
                    Url      = $"https://dev.azure.com/{g.AzureDevOpsOrg}/{proj}/_workitems/edit/{i?["id"]?.GetValue<int>()}",
                    Source   = "azuredevops"
                };
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Azure DevOps fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchAsanaAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.AsanaToken) || string.IsNullOrEmpty(g.AsanaProjectId)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", g.AsanaToken);
            var json = await http.GetStringAsync(
                $"https://app.asana.com/api/1.0/tasks?project={g.AsanaProjectId}&completed_since=now&opt_fields=gid,name,notes,completed&limit=50");
            return JsonNode.Parse(json)?["data"]?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = i?["gid"]?.GetValue<string>() ?? "",
                Title       = i?["name"]?.GetValue<string>() ?? "",
                Description = i?["notes"]?.GetValue<string>() ?? "",
                Status      = i?["completed"]?.GetValue<bool>() == true ? "completed" : "active",
                Type        = "task", Source = "asana",
                Url         = $"https://app.asana.com/0/{g.AsanaProjectId}/{i?["gid"]?.GetValue<string>()}"
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Asana fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchClickUpAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.ClickUpToken) || string.IsNullOrEmpty(g.ClickUpListId)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Add("Authorization", g.ClickUpToken);
            var json = await http.GetStringAsync(
                $"https://api.clickup.com/api/v2/list/{g.ClickUpListId}/task?include_closed=false&page=0");
            return JsonNode.Parse(json)?["tasks"]?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = i?["id"]?.GetValue<string>() ?? "",
                Title       = i?["name"]?.GetValue<string>() ?? "",
                Description = i?["description"]?.GetValue<string>() ?? "",
                Status      = i?["status"]?["status"]?.GetValue<string>() ?? "",
                Priority    = i?["priority"]?["priority"]?.GetValue<string>() ?? "",
                Type        = "task", Source = "clickup",
                Url         = i?["url"]?.GetValue<string>()
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "ClickUp fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchYouTrackAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.YouTrackUrl) || string.IsNullOrEmpty(g.YouTrackToken)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", g.YouTrackToken);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var q    = string.IsNullOrEmpty(g.YouTrackProject) ? "State: -Resolved" : $"project: {g.YouTrackProject} State: -Resolved";
            var json = await http.GetStringAsync(
                $"{g.YouTrackUrl.TrimEnd('/')}/api/issues?query={Uri.EscapeDataString(q)}&fields=id,idReadable,summary,description,priority(name),type(name),state(name)&$top=50");
            return JsonNode.Parse(json)?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = i?["idReadable"]?.GetValue<string>() ?? "",
                Title       = i?["summary"]?.GetValue<string>() ?? "",
                Description = i?["description"]?.GetValue<string>() ?? "",
                Status      = i?["state"]?["name"]?.GetValue<string>() ?? "",
                Type        = i?["type"]?["name"]?.GetValue<string>()?.ToLower() ?? "issue",
                Priority    = i?["priority"]?["name"]?.GetValue<string>() ?? "",
                Url         = $"{g.YouTrackUrl.TrimEnd('/')}/issue/{i?["idReadable"]?.GetValue<string>()}",
                Source      = "youtrack"
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "YouTrack fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchTrelloAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.TrelloKey) || string.IsNullOrEmpty(g.TrelloToken) || string.IsNullOrEmpty(g.TrelloBoardId)) return new();
        try
        {
            var http = _http.CreateClient();
            var json = await http.GetStringAsync(
                $"https://api.trello.com/1/boards/{g.TrelloBoardId}/cards/open?fields=id,name,desc,url&key={g.TrelloKey}&token={g.TrelloToken}");
            return JsonNode.Parse(json)?.AsArray().Select(i => new DevToolsTaskItem
            {
                Id          = i?["id"]?.GetValue<string>() ?? "",
                Title       = i?["name"]?.GetValue<string>() ?? "",
                Description = i?["desc"]?.GetValue<string>() ?? "",
                Status      = "open", Type = "card", Source = "trello",
                Url         = i?["url"]?.GetValue<string>()
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Trello fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchShortcutAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.ShortcutToken)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Add("Shortcut-Token", g.ShortcutToken);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var filter = string.IsNullOrEmpty(g.ShortcutWorkflowStateId) ? "{}"
                : $"{{\"workflow_state_id\": {int.Parse(g.ShortcutWorkflowStateId)}}}";
            var json = await (await http.PostAsync("https://api.app.shortcut.com/api/v3/stories/search",
                new StringContent(filter, Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync();
            return JsonNode.Parse(json)?.AsArray().Take(50).Select(i => new DevToolsTaskItem
            {
                Id          = $"sc-{i?["id"]?.GetValue<int>()}",
                Title       = i?["name"]?.GetValue<string>() ?? "",
                Description = i?["description"]?.GetValue<string>() ?? "",
                Status      = i?["workflow_state_id"]?.GetValue<int>().ToString() ?? "",
                Type        = i?["story_type"]?.GetValue<string>() ?? "story", Source = "shortcut",
                Url         = i?["app_url"]?.GetValue<string>()
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Shortcut fetch failed"); return new(); }
    }

    private async Task<List<DevToolsTaskItem>> FetchNotionAsync(DevToolsAdminGlobalDto g)
    {
        if (string.IsNullOrEmpty(g.NotionToken) || string.IsNullOrEmpty(g.NotionDatabaseId)) return new();
        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", g.NotionToken);
            http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
            var body = new StringContent("""{"page_size": 50}""", Encoding.UTF8, "application/json");
            var json = await (await http.PostAsync(
                $"https://api.notion.com/v1/databases/{g.NotionDatabaseId}/query", body)).Content.ReadAsStringAsync();
            return JsonNode.Parse(json)?["results"]?.AsArray().Select(i =>
            {
                var props = i?["properties"];
                string Title(string key) => props?[key]?["title"]?[0]?["text"]?["content"]?.GetValue<string>()
                    ?? props?[key]?["rich_text"]?[0]?["text"]?["content"]?.GetValue<string>() ?? "";
                return new DevToolsTaskItem
                {
                    Id     = i?["id"]?.GetValue<string>() ?? "",
                    Title  = Title("Name") is var t && !string.IsNullOrEmpty(t) ? t : Title("title"),
                    Status = props?["Status"]?["status"]?["name"]?.GetValue<string>() ?? "",
                    Type   = "page", Source = "notion",
                    Url    = i?["url"]?.GetValue<string>()
                };
            }).ToList() ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Notion fetch failed"); return new(); }
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string ExtractJiraAdf(JsonNode? node)
    {
        if (node is null) return "";
        try
        {
            var sb = new StringBuilder();
            foreach (var block in node["content"]?.AsArray() ?? new())
                foreach (var inline in block?["content"]?.AsArray() ?? new())
                { var t = inline?["text"]?.GetValue<string>(); if (!string.IsNullOrEmpty(t)) sb.Append(t).Append(' '); }
            return sb.ToString().Trim();
        }
        catch { return ""; }
    }

    public static string LinearPriority(int p) => p switch { 1 => "Urgent", 2 => "High", 3 => "Medium", 4 => "Low", _ => "No priority" };
    public static string Coalesce(string? a, string? b) => !string.IsNullOrEmpty(a) ? a : b ?? "";
}
