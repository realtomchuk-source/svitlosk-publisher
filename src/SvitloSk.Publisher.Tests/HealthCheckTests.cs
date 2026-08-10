using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SvitloSk.Publisher.Host;
using SvitloSk.Publisher.Runtime;
using SvitloSk.Publisher.Runtime.Persistence;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DbConnectionString { get; set; } = "Host=localhost;Database=svitlosk_test_dummy;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Telegram:BotToken", "valid-token-for-test"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Telegram:TargetChatId", "valid-chat-for-test")
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<SvitloSkDbContext>));
            if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);
            
            services.AddDbContext<SvitloSkDbContext>(options => options.UseNpgsql(DbConnectionString));
            // Remove the real background workers to prevent infinite polling and external calls during test
            var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var hostedService in hostedServices)
            {
                if (hostedService.ImplementationType == typeof(SynchronizationWorker) ||
                    hostedService.ImplementationType == typeof(OutboxDispatcherWorker))
                {
                    services.Remove(hostedService);
                }
            }
        });
    }
}

public class HealthCheckTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _postgresFixture;

    public HealthCheckTests(PostgresTestFixture postgresFixture)
    {
        _postgresFixture = postgresFixture;
    }

    [Fact]
    public async Task Liveness_ReturnsOk_IndependentOfDatabase()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory
        {
            DbConnectionString = "Host=invalid_host;Database=invalid_db;Username=postgres;Password=postgres" // DB is down
        };
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReturnsOk_WhenDatabaseIsAvailable()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory
        {
            DbConnectionString = _postgresFixture.ConnectionString
        };
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReturnsServiceUnavailable_WhenDatabaseIsUnavailable()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory
        {
            DbConnectionString = "Host=invalid_host;Database=invalid_db;Username=postgres;Password=postgres" // DB is down
        };
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
