using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using SvitloSk.Publisher.Runtime.Persistence;

namespace SvitloSk.Publisher.Tests;

public class PostgresTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;

    public PostgresTestFixture()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("svitlosk_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();
    }

    public string ConnectionString => _dbContainer.GetConnectionString();

    public DbContextOptions<SvitloSkDbContext> GetDbContextOptions()
    {
        return new DbContextOptionsBuilder<SvitloSkDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Run migrations
        var options = GetDbContextOptions();
        using var context = new SvitloSkDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}

[CollectionDefinition("PostgresCollection")]
public class PostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
