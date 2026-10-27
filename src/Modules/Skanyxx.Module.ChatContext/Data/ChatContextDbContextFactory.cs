using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.ChatContext.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class ChatContextDbContextFactory : IDesignTimeDbContextFactory<ChatContextDbContext>
{
    public ChatContextDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ChatContextDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(ChatContextDbContext.MigrationsTable))
            .Options);
}
