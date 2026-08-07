using System;
using Xunit;

namespace SvitloSk.Publisher.Domain.Tests;

public class DomainTests
{
    [Fact]
    public void Territory_Should_Be_Immutable_Record()
    {
        var territory = new Territory("T1", "Starokostiantyniv");
        Assert.Equal("T1", territory.Id);
        Assert.Equal("Starokostiantyniv", territory.CanonicalName);
    }

    [Fact]
    public void Publication_Should_Be_Immutable_Record()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var publication = new Publication(
            id, 
            "T1", 
            PublicationClassification.Persistent,
            PublicationType.Text,
            now,
            "hash123",
            new[] { "Address 1" },
            true);
        
        Assert.Equal(id, publication.Id);
        Assert.Equal("T1", publication.TerritoryId);
        Assert.Equal(PublicationClassification.Persistent, publication.Classification);
        Assert.Equal(PublicationType.Text, publication.Type);
        Assert.Equal(now, publication.CreatedAt);
        Assert.Equal("hash123", publication.ContentHash);
        Assert.Single(publication.Addresses!);
        Assert.True(publication.HasTomorrowForecast);
    }

    [Fact]
    public void Edition_Should_Manage_State_Transitions()
    {
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 6));
        Assert.Equal(EditionState.Created, edition.State);

        edition.Activate();
        Assert.Equal(EditionState.Active, edition.State);

        edition.Close();
        Assert.Equal(EditionState.Closed, edition.State);
    }

    [Fact]
    public void Edition_Should_Not_Allow_Activation_When_Closed()
    {
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 6));
        edition.Close();

        Assert.Throws<InvalidOperationException>(() => edition.Activate());
    }

    [Fact]
    public void Edition_Should_Manage_Publications()
    {
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 6));
        var publication = new Publication(Guid.NewGuid(), "T1", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash");
        
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        edition.AddPackage(pkg);
        pkg.AddPublication(publication);
        
        Assert.Single(edition.Packages);
        Assert.Single(edition.Packages.First().Publications);
        Assert.Contains(publication, edition.Packages.First().Publications);
    }

    [Fact]
    public void Edition_Should_Not_Allow_Adding_Publications_When_Closed()
    {
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 6));
        edition.Close();
        
        var publication = new Publication(Guid.NewGuid(), "T1", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash");
        
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        Assert.Throws<InvalidOperationException>(() => edition.AddPackage(pkg));
    }

    [Fact]
    public void Edition_Should_Support_Removing_Publications()
    {
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 6));
        var publication = new Publication(Guid.NewGuid(), "T1", PublicationClassification.Ephemeral, PublicationType.Text, DateTimeOffset.UtcNow, "hash");
        
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        edition.AddPackage(pkg);
        pkg.AddPublication(publication);

        // Act
        pkg.RemovePublication(publication);

        // Assert
        Assert.Empty(pkg.Publications);
    }

    [Fact]
    public void Edition_Should_Not_Allow_Removing_Packages_When_Closed()
    {
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 6));
        
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        edition.AddPackage(pkg);
        
        // Act
        edition.Close();

        // Assert
        Assert.Throws<InvalidOperationException>(() => edition.RemovePackage(pkg));
    }
}
