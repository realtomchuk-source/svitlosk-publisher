using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;
using SvitloSk.Publisher.Execution;

namespace SvitloSk.Publisher.Tests;

public class PublicationPackageTests
{
    [Fact]
    public void ScenarioA_EditionContainsOnePackage_AssemblySucceeds()
    {
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        var pkg = new PublicationPackage(Guid.NewGuid(), "Single Package");
        edition.AddPackage(pkg);
        
        var pub1 = new Publication(Guid.NewGuid(), "T1", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "c1");
        pkg.AddPublication(pub1);

        var strategy = new CanonicalOrderingStrategy();
        var assembly = new EditionAssembly(strategy);
        
        var artifacts = new List<PublicationArtifact>
        {
            new PublicationArtifact(pub1.Id, "T1", PublicationClassification.Persistent, "Content 1")
        };

        var result = assembly.Assemble(edition, artifacts);
        Assert.Single(result.OrderedContent);
        Assert.Equal("Content 1", result.OrderedContent.First());
    }

    [Fact]
    public void ScenarioB_EditionContainsMultiplePackages_OrderingPreserved()
    {
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        
        var pkg1 = new PublicationPackage(Guid.NewGuid(), "B_Package");
        var pkg2 = new PublicationPackage(Guid.NewGuid(), "A_Package");
        
        edition.AddPackage(pkg1);
        edition.AddPackage(pkg2);
        
        var pub1 = new Publication(Guid.NewGuid(), "T1", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "c1");
        pkg1.AddPublication(pub1);

        var pub2 = new Publication(Guid.NewGuid(), "T2", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "c2");
        pkg2.AddPublication(pub2);

        var strategy = new CanonicalOrderingStrategy();
        var assembly = new EditionAssembly(strategy);
        
        var artifacts = new List<PublicationArtifact>
        {
            new PublicationArtifact(pub1.Id, "T1", PublicationClassification.Persistent, "Content 1"),
            new PublicationArtifact(pub2.Id, "T2", PublicationClassification.Persistent, "Content 2")
        };

        var result = assembly.Assemble(edition, artifacts);
        
        Assert.Equal(2, result.OrderedContent.Count());
        var orderedList = result.OrderedContent.ToList();
        // T1 comes before T2 in canonical EditorialOrder (Alphabetical by TerritoryId)
        Assert.Equal("Content 1", orderedList[0]);
        Assert.Equal("Content 2", orderedList[1]);
    }

    [Fact]
    public void ScenarioC_PackageContainsNoPublications_OmittedFromAssembly()
    {
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        var pkg = new PublicationPackage(Guid.NewGuid(), "Empty Package");
        edition.AddPackage(pkg);
        
        var strategy = new CanonicalOrderingStrategy();
        var assembly = new EditionAssembly(strategy);
        
        var artifacts = new List<PublicationArtifact>();

        var result = assembly.Assemble(edition, artifacts);
        Assert.Empty(result.OrderedContent);
    }
}
