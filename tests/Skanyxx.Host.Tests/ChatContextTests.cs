using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The chat index end to end (D260–D265): Chat → chatcontext through the real Host and modules folder, against
/// <see cref="FakeKAgent"/>. A new chat on a ticket is given the earlier chat on it, the response names it, and another
/// person is given nothing of it.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ChatContextTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string EmployeeEmail = "erin@skanyxx.example";
    private const string EmployeePassword = "a long employee passphrase";

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    private static async Task<JsonElement> ChatAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/chat", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private string LastMessageSent() =>
        _kagent.CallsTo("POST", "/api/a2a/kagent/seed/").Last().Body!["params"]!["message"]!["parts"]![0]!["text"]!.GetValue<string>();

    [Fact]
    public async Task ANewChatOnATicket_IsGivenTheEarlierChat_AndSaysSo()
    {
        var owner = await _host.OwnerAsync();
        _kagent.Answer = "EU refunds wait for the Friday bank batch.";
        var first = await ChatAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Why are EU refunds slow?", ticketKeys = new[] { "SDB-58" } });
        Assert.Empty(first.GetProperty("relatedSessions").EnumerateArray());
        Assert.Equal("Why are EU refunds slow?", LastMessageSent());

        _kagent.Answer = "Follow-up done.";
        var second = await ChatAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Continue SDB-58", ticketKeys = new[] { "SDB-58" } });

        var used = Assert.Single(second.GetProperty("relatedSessions").EnumerateArray());
        Assert.Equal(first.GetProperty("conversationId").GetString(), used.GetProperty("sessionId").GetString());
        Assert.Equal("EU refunds wait for the Friday bank batch.", used.GetProperty("outcome").GetString());
        var sent = LastMessageSent();
        Assert.StartsWith("[Related prior work", sent);
        Assert.EndsWith("Continue SDB-58", sent);
        Assert.Contains("EU refunds wait for the Friday bank batch.", sent);

        // A later turn of the same conversation carries no context.
        await ChatAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Thanks", conversationId = second.GetProperty("conversationId").GetString() });
        Assert.Equal("Thanks", LastMessageSent());
    }

    [Fact]
    public async Task AnotherPerson_IsGivenNothingOfAPrivateChat_AndSeesOnlyTheirOwnHistory()
    {
        var owner = await _host.OwnerAsync();
        var employee = await EmployeeAsync();
        var ownerChat = await ChatAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Private refund question on SDB-58", ticketKeys = new[] { "SDB-58" } });

        var employeeChat = await ChatAsync(employee, new { agentNamespace = "kagent", agentName = "seed", message = "Refund question on SDB-58", ticketKeys = new[] { "SDB-58" } });

        Assert.Empty(employeeChat.GetProperty("relatedSessions").EnumerateArray());
        Assert.DoesNotContain("Private refund question", LastMessageSent());
        var history = await employee.GetFromJsonAsync<JsonElement>("/api/chat/sessions?mine=true", TestContext.Current.CancellationToken);
        Assert.Equal([employeeChat.GetProperty("conversationId").GetString()], history.EnumerateArray().Select(s => s.GetProperty("sessionId").GetString()));
        var byTicket = await employee.GetFromJsonAsync<JsonElement>("/api/chat/tickets/SDB-58", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(byTicket.GetProperty("sessions").EnumerateArray(), s => s.GetProperty("sessionId").GetString() == ownerChat.GetProperty("conversationId").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await employee.GetAsync($"/api/chat/sessions/{ownerChat.GetProperty("conversationId").GetString()}", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>
    /// requirements.md §3.4: a reload continues the same session. The page keeps the open conversation in its URL and
    /// in this tab's storage, and reopens it from the server (ChatPanelTests drives the API side).
    /// </summary>
    [Fact]
    public async Task TheChatPage_ContinuesTheConversationAcrossAReload()
    {
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });

        var page = await (await browser.GetAsync("/Chat/kagent/seed/a-conversation")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var script = await (await browser.GetAsync("/js/chat.js")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("data-session=\"a-conversation\"", page);
        Assert.Contains("sessionStorage.setItem(STORE_KEY", script);
        Assert.Contains("sessionStorage.getItem(STORE_KEY)", script);
        Assert.Contains("await openConversation(conversationId, push)", script);
        Assert.Contains("/api/chat/conversations", script);
    }

    /// <summary>requirements.md §1.5, §3.5 (D273): cost carries the disclaimer, and no model's price is assumed (was GPT-4's for every model).</summary>
    [Fact]
    public async Task TheChatPage_ShowsCostAsNa_WithTheDisclaimer()
    {
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });

        var page = await (await browser.GetAsync("/Chat")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("<span class=\"stat-value\" id=\"estCost\" title=\"No price is known for this agent's model\">n/a</span>", page);
        Assert.Contains("Cost is informational only, not accurate.", page);
        Assert.Contains("Your provider's bill is authoritative.", page);
        Assert.DoesNotContain("GPT-4", page);
        Assert.DoesNotContain("estCost').textContent", page);
    }

    // Chat creates each kagent session as the person, so a reload reads it back as them (D274, D280; the owner's default
    // kagent read is the console's own user, D124, which does not have it). Nobody else's conversation is readable.
    [Fact]
    public async Task TheChatPage_ReopensAConversation_AsItsOwnerOnly()
    {
        var owner = await _host.OwnerAsync();
        var chat = await ChatAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Why are EU refunds slow?" });
        var id = chat.GetProperty("conversationId").GetString()!;
        var employee = await EmployeeAsync();
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });

        var script = await (await browser.GetAsync("/js/chat.js")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var asOwner = await browser.GetAsync($"/api/chat/conversations/{id}");
        var asEmployee = await employee.GetAsync($"/api/chat/conversations/{id}", TestContext.Current.CancellationToken);

        Assert.Contains("call('GET', `/api/chat/conversations/${encodeURIComponent(id)}`)", script);
        Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
        Assert.Equal("Why are EU refunds slow?", (await asOwner.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("items")[0].GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.NotFound, asEmployee.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTicketKey_IsRefusedBeforeKagent()
    {
        var owner = await _host.OwnerAsync();

        var response = await owner.PostAsJsonAsync("/api/chat",
            new { agentNamespace = "kagent", agentName = "seed", message = "hi", ticketKeys = new[] { "drop table" } }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/sessions"));
    }

    private async Task<HttpClient> EmployeeAsync()
    {
        var owner = await _host.OwnerAsync();
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email = EmployeeEmail, roles = new[] { "employee" } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;
        (await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = EmployeePassword })).EnsureSuccessStatusCode();
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = EmployeeEmail, password = EmployeePassword });
        signIn.EnsureSuccessStatusCode();
        return _host.Client((await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }
}
