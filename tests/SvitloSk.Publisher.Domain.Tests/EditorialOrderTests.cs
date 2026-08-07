using System;
using System.Linq;
using Xunit;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Domain.Tests;

public class EditorialOrderTests
{
    [Fact]
    public void EditorialOrder_OrdersPublicationsCorrectly()
    {
        // Arrange
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 7));
        var package = new PublicationPackage(Guid.NewGuid(), "Default");
        edition.AddPackage(package);

        var pubTech = new Publication(Guid.NewGuid(), "Tech", PublicationClassification.Ephemeral, PublicationType.Technical, DateTimeOffset.UtcNow, "hash");
        var pubDistrictA = new Publication(Guid.NewGuid(), "A_District", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash");
        var pubStaro = new Publication(Guid.NewGuid(), "Starokostiantyniv", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash");
        var pubTomorrow = new Publication(Guid.NewGuid(), "Tomorrow", PublicationClassification.Ephemeral, PublicationType.Tomorrow, DateTimeOffset.UtcNow, "hash");
        var pubDistrictB = new Publication(Guid.NewGuid(), "B_District", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash");

        // Add in random order
        package.AddPublication(pubDistrictB);
        package.AddPublication(pubTech);
        package.AddPublication(pubStaro);
        package.AddPublication(pubTomorrow);
        package.AddPublication(pubDistrictA);

        // Act
        var order = new EditorialOrder(edition);

        // Assert
        Assert.Equal(5, order.OrderedPublicationIds.Count);
        Assert.Equal(pubTomorrow.Id, order.OrderedPublicationIds[0]);
        Assert.Equal(pubStaro.Id, order.OrderedPublicationIds[1]);
        Assert.Equal(pubDistrictA.Id, order.OrderedPublicationIds[2]);
        Assert.Equal(pubDistrictB.Id, order.OrderedPublicationIds[3]);
        Assert.Equal(pubTech.Id, order.OrderedPublicationIds[4]);
    }

    [Fact]
    public void EditorialOrder_OrdersPackagesAndPublications()
    {
        // Arrange
        var edition = new Edition(Guid.NewGuid(), new DateOnly(2026, 8, 7));
        var pkgB = new PublicationPackage(Guid.NewGuid(), "B_Package");
        var pkgA = new PublicationPackage(Guid.NewGuid(), "A_Package");
        
        edition.AddPackage(pkgB);
        edition.AddPackage(pkgA);

        var pubTech = new Publication(Guid.NewGuid(), "Tech", PublicationClassification.Ephemeral, PublicationType.Technical, DateTimeOffset.UtcNow, "hash");
        var pubStaro = new Publication(Guid.NewGuid(), "Starokostiantyniv", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash");
        
        pkgB.AddPublication(pubTech);
        pkgA.AddPublication(pubStaro);

        // Act
        var order = new EditorialOrder(edition);

        // Assert
        Assert.Equal(2, order.OrderedPackages.Count);
        
        // A_Package comes first
        Assert.Equal("A_Package", order.OrderedPackages[0].Name);
        Assert.Single(order.OrderedPackages[0].Publications);
        Assert.Equal(pubStaro.Id, order.OrderedPackages[0].Publications.First().Id);

        // B_Package comes second
        Assert.Equal("B_Package", order.OrderedPackages[1].Name);
        Assert.Single(order.OrderedPackages[1].Publications);
        Assert.Equal(pubTech.Id, order.OrderedPackages[1].Publications.First().Id);
    }
}
