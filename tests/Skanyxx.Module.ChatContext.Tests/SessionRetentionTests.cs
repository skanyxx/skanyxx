using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Domain;
using Skanyxx.Module.ChatContext.Tests.Infrastructure;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>D350: the Settings page's chat history retention deletes indexed sessions older than it; 0 keeps them all.</summary>
public sealed class SessionRetentionTests(PostgresFixture postgres) : ChatContextTestBase(postgres)
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?> { ["ChatContext:SessionRetentionDays"] = "30" };

    [Fact]
    public async Task SessionsOlderThanTheRetention_AreDeleted()
    {
        await using (var scope = App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatContextDbContext>();
            db.Sessions.AddRange(Session("old", DateTime.UtcNow.AddDays(-31)), Session("recent", DateTime.UtcNow.AddDays(-29)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var deleted = await App.Services.GetRequiredService<SessionRetention>().RunOnceAsync(TestContext.Current.CancellationToken);

        await using var check = App.Services.CreateAsyncScope();
        Assert.Equal(1, deleted);
        Assert.Equal(["recent"], await check.ServiceProvider.GetRequiredService<ChatContextDbContext>().Sessions.Select(s => s.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static ChatSession Session(string id, DateTime updated) => new()
    {
        Id = id, UserId = "ana", AgentRef = "kagent/seed", Title = id, Scope = "personal:ana", CreatedAt = updated, UpdatedAt = updated
    };
}
