using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using SvitloSk.Publisher.Runtime;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class RealOutagesSkInputPackageProviderTests
{
    private (HttpClient, Mock<HttpMessageHandler>) CreateHttpClient(string jsonContent, HttpStatusCode jsonStatus, string textContent, HttpStatusCode textStatus)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains(".json")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = jsonStatus,
                Content = new StringContent(jsonContent)
            });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("today.txt")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = textStatus,
                Content = new StringContent(textContent)
            });

        return (new HttpClient(handlerMock.Object), handlerMock);
    }

    [Fact]
    public async Task GetLatestAsync_ValidJsonAndText_ReturnsInputPackage()
    {
        var targetDate = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv")).ToString("yyyy-MM-dd");
        var jsonContent = $"{{\"date\": \"{targetDate}\", \"mode\": \"schedule\"}}";
        var textContent = @"[Місто Старокостянтинів]
м. Старокостянтинів | з 09:00 до 17:00
- вул. Богуна: буд. 1, 3/1, 4";

        var (client, _) = CreateHttpClient(jsonContent, HttpStatusCode.OK, textContent, HttpStatusCode.OK);
        
        var options = Options.Create(new InputSourcesOptions
        {
            Json = new JsonSourceOptions { BaseUrl = "http://fake-json" },
            Text = new TextSourceOptions { BaseUrl = "http://fake-text" }
        });

        var provider = new RealOutagesSkInputPackageProvider(client, options, NullLogger<RealOutagesSkInputPackageProvider>.Instance);

        var package = await provider.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(package);
        Assert.Single(package.Events);
        
        var ev = package.Events.First();
        Assert.Equal("Місто Старокостянтинів", ev.Settlement);
        Assert.Single(ev.Streets);
        Assert.Single(ev.Intervals);
    }

    [Fact]
    public async Task GetLatestAsync_JsonUnavailable_ThrowsException()
    {
        var (client, _) = CreateHttpClient("", HttpStatusCode.NotFound, "text", HttpStatusCode.OK);
        
        var options = Options.Create(new InputSourcesOptions());
        var provider = new RealOutagesSkInputPackageProvider(client, options, NullLogger<RealOutagesSkInputPackageProvider>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetLatestAsync(CancellationToken.None));
    }
    
    [Fact]
    public async Task GetLatestAsync_TextUnavailable_ThrowsException()
    {
        var targetDate = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv")).ToString("yyyy-MM-dd");
        var jsonContent = $"{{\"date\": \"{targetDate}\", \"mode\": \"schedule\"}}";
        
        var (client, _) = CreateHttpClient(jsonContent, HttpStatusCode.OK, "", HttpStatusCode.NotFound);
        
        var options = Options.Create(new InputSourcesOptions());
        var provider = new RealOutagesSkInputPackageProvider(client, options, NullLogger<RealOutagesSkInputPackageProvider>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestAsync_DateMismatch_ThrowsInvalidOperationException()
    {
        var jsonContent = "{\"date\": \"2000-01-01\", \"mode\": \"schedule\"}";
        var (client, _) = CreateHttpClient(jsonContent, HttpStatusCode.OK, "text", HttpStatusCode.OK);
        
        var options = Options.Create(new InputSourcesOptions());
        var provider = new RealOutagesSkInputPackageProvider(client, options, NullLogger<RealOutagesSkInputPackageProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetLatestAsync(CancellationToken.None));
    }
    
    [Fact]
    public async Task GetLatestAsync_MalformedJson_ThrowsException()
    {
        var (client, _) = CreateHttpClient("invalid-json", HttpStatusCode.OK, "text", HttpStatusCode.OK);
        
        var options = Options.Create(new InputSourcesOptions());
        var provider = new RealOutagesSkInputPackageProvider(client, options, NullLogger<RealOutagesSkInputPackageProvider>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetLatestAsync(CancellationToken.None));
    }
}
