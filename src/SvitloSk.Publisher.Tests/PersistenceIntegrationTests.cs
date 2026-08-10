using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Xunit;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Runtime.Persistence;

namespace SvitloSk.Publisher.Tests;

public class PersistenceIntegrationTests : IDisposable
{
    private readonly DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SvitloSkDbContext> _options;

    public PersistenceIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SvitloSkDbContext>()
            .UseSqlite(_connection)
            .Options;
            
        using var context = new SvitloSkDbContext(_options);
        context.Database.EnsureCreated();
    }
    
    [Fact]
    public void Edition_CanBeSavedAndLoaded()
    {
        // Note: SQLite in-memory is used for relational behavior testing only.
        // Full PostgreSQL integration test remains pending.
        
        using var setupContext = new SvitloSkDbContext(_options);
        
        var editionId = Guid.NewGuid();
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var edition = new Edition(editionId, targetDate);
        
        var pkg = new PublicationPackage(Guid.NewGuid(), "Morning Package");
        var pub = new Publication(
            Guid.NewGuid(),
            "territory1",
            PublicationClassification.Persistent,
            PublicationType.Text,
            _now,
            "hash123",
            new[] { "Address 1", "Address 2" },
            true,
            "scheduleHash1"
        );
        
        pkg.AddPublication(pub);
        edition.AddPackage(pkg);

        var repo = new EfEditionRepository(setupContext);
        repo.Save(edition);
        setupContext.SaveChanges();

        // Assert surviving process restart / new DbContext
        using var verifyContext = new SvitloSkDbContext(_options);
        
        var verifyRepo = new EfEditionRepository(verifyContext);
        var loadedEdition = verifyRepo.GetById(editionId);

        Assert.NotNull(loadedEdition);
        Assert.Equal(targetDate, loadedEdition.TargetDate);
        Assert.Single(loadedEdition.Packages);
        
        var loadedPkg = loadedEdition.Packages.First();
        Assert.Equal(pkg.Id, loadedPkg.Id);
        Assert.Single(loadedPkg.Publications);
        
        var loadedPub = loadedPkg.Publications.First();
        Assert.Equal(pub.Id, loadedPub.Id);
        Assert.Equal("hash123", loadedPub.ContentHash);
        Assert.NotNull(loadedPub.Addresses);
        Assert.Equal(2, loadedPub.Addresses.Count);
    }
    
    [Fact]
    public void ExternalIdentity_CanBeSavedAndResolved()
    {
        using var setupContext = new SvitloSkDbContext(_options);

        var resolver = new EfExternalPublicationIdentityResolver(setupContext);
        
        var pubId = Guid.NewGuid().ToString();
        var extId = "chat1_msg2";
        
        resolver.RecordExternalIdentity(pubId, extId);
        setupContext.SaveChanges();

        using var verifyContext = new SvitloSkDbContext(_options);
        var verifyResolver = new EfExternalPublicationIdentityResolver(verifyContext);
        
        var loadedExtId = verifyResolver.ResolveExternalIdentity(pubId);
        
        Assert.Equal(extId, loadedExtId);
    }
    
    public void Dispose()
    {
        _connection.Dispose();
    }
}
