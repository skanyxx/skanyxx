using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Skanyxx.Module.Investigate.Data;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>A model change without a migration would pass every other test and fail the first insert in production.</summary>
public sealed class MigrationDriftTests
{
    [Fact]
    public void TheModel_MatchesTheMigrations()
    {
        using var db = new InvestigateDbContext(new DbContextOptionsBuilder<InvestigateDbContext>().UseNpgsql("Host=unused").Options);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var finalized = db.GetService<IModelRuntimeInitializer>().Initialize(((IMutableModel)snapshot).FinalizeModel());

        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(
            finalized.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
    }
}
