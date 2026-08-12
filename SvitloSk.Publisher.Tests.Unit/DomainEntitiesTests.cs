using System;
using SvitloSk.Publisher.Core.Domain;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit;

public class DomainEntitiesTests
{
    [Fact]
    public void CreateTerritory_WithValidId_ShouldSucceed()
    {
        var territory = new Territory("starokostiantyniv");
        Assert.Equal("starokostiantyniv", territory.Identifier);
    }

    [Fact]
    public void CreateTerritory_WithEmptyId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => new Territory(""));
        Assert.Throws<ArgumentException>(() => new Territory("   "));
    }

    [Fact]
    public void CreatePublication_WithValidFields_ShouldSucceed()
    {
        var id = Guid.NewGuid();
        var time = DateTime.UtcNow;
        var publication = new Publication(id, "starokostiantyniv", PublicationType.Text, time, "abc-hash");

        Assert.Equal(id, publication.PublicationId);
        Assert.Equal("starokostiantyniv", publication.TerritoryIdentifier);
        Assert.Equal(PublicationType.Text, publication.PublicationType);
        Assert.Equal(time, publication.CreatedAt);
        Assert.Equal("abc-hash", publication.ContentHash);
        Assert.Equal(PublicationState.Created, publication.State);
        Assert.True(publication.IsPersistent);
    }

    [Fact]
    public void CreatePublisherArtifact_WithValidFields_ShouldSucceed()
    {
        var artId = Guid.NewGuid();
        var pubId = Guid.NewGuid();
        var artifact = new PublisherArtifact(artId, pubId, "hash123");

        Assert.Equal(artId, artifact.PublisherArtifactId);
        Assert.Equal(pubId, artifact.PublicationId);
        Assert.Equal("hash123", artifact.ContentHash);
    }

    [Fact]
    public void Edition_AddPublication_ShouldStoreIt()
    {
        var edition = new Edition("2026-08-12");
        var publication = new Publication(Guid.NewGuid(), "starokostiantyniv", PublicationType.Text, DateTime.UtcNow, "hash1");
        
        edition.AddPublication(publication);

        Assert.Single(edition.Publications);
        Assert.Contains(publication, edition.Publications);
    }

    [Fact]
    public void Edition_TransitionToForbidden_ShouldThrow()
    {
        var edition = new Edition("2026-08-12", EditionState.Closed);
        
        Assert.Throws<InvalidOperationException>(() => edition.TransitionTo(EditionState.Active));
    }
}
