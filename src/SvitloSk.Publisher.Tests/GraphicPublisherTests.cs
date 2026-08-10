using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;
using SvitloSk.Publisher.Execution;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class GraphicPublisherTests
{
    private GraphicPublisher CreatePublisher()
    {
        var orderingStrategy = new CanonicalOrderingStrategy();
        var assembly = new EditionAssembly(orderingStrategy);
        return new GraphicPublisher(NullLogger<GraphicPublisher>.Instance, assembly);
    }

    [Fact]
    public void Publish_SinglePublication_GeneratesSingleGraphicPublication()
    {
        // Arrange
        var publisher = CreatePublisher();
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        var package = new PublicationPackage(Guid.NewGuid(), "Morning Package");
        
        var pubId = Guid.NewGuid();
        var publication = new Publication(pubId, "Kyiv", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "Some content");
        package.AddPublication(publication);
        edition.AddPackage(package);
        edition.Activate();

        var graphicData = new Dictionary<string, string> { { "Q1", "Data" } };
        var artifacts = new List<PublicationArtifact>
        {
            new PublicationArtifact(pubId, "Kyiv", PublicationClassification.Persistent, "Kyiv Content", graphicData)
        };

        // Act
        var result = publisher.Publish(edition, artifacts);

        // Assert
        Assert.Single(result);
        var graphic = result.First();
        Assert.Equal(pubId, graphic.PublicationId);
        Assert.Equal("Kyiv", graphic.Territory);
        Assert.Equal(PublicationClassification.Persistent, graphic.Classification);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(graphicData), graphic.GraphicContent);
    }

    [Fact]
    public void Publish_MultiplePublications_GraphicsGeneratedInIdenticalOrder()
    {
        // Arrange
        var publisher = CreatePublisher();
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        var package = new PublicationPackage(Guid.NewGuid(), "Morning Package");
        
        var pubId1 = Guid.NewGuid();
        var pubId2 = Guid.NewGuid();
        var publication1 = new Publication(pubId1, "Kyiv", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "Content 1");
        var publication2 = new Publication(pubId2, "Lviv", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "Content 2");
        package.AddPublication(publication1);
        package.AddPublication(publication2);
        edition.AddPackage(package);
        edition.Activate();

        var graphicDataKyiv = new Dictionary<string, string> { { "Q1", "K" } };
        var graphicDataLviv = new Dictionary<string, string> { { "Q2", "L" } };
        
        var artifacts = new List<PublicationArtifact>
        {
            new PublicationArtifact(pubId2, "Lviv", PublicationClassification.Persistent, "Lviv Content", graphicDataLviv),
            new PublicationArtifact(pubId1, "Kyiv", PublicationClassification.Persistent, "Kyiv Content", graphicDataKyiv)
        };

        // Act
        var result = publisher.Publish(edition, artifacts).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(pubId1, result[0].PublicationId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(graphicDataKyiv), result[0].GraphicContent);
        Assert.Equal(pubId2, result[1].PublicationId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(graphicDataLviv), result[1].GraphicContent);
    }

    [Fact]
    public void Publish_EmptyEdition_EmptyResult()
    {
        // Arrange
        var publisher = CreatePublisher();
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        edition.Activate();

        var artifacts = new List<PublicationArtifact>();

        // Act
        var result = publisher.Publish(edition, artifacts);

        // Assert
        Assert.Empty(result);
    }
}
