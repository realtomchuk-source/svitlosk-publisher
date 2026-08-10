using Microsoft.EntityFrameworkCore;
using SvitloSk.Publisher.Domain;
using System.Text.Json;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Runtime.Persistence;

public class SvitloSkDbContext : DbContext
{
    public SvitloSkDbContext(DbContextOptions<SvitloSkDbContext> options) : base(options) { }

    public DbSet<Edition> Editions { get; set; } = null!;
    public DbSet<PublicationPackage> PublicationPackages { get; set; } = null!;
    public DbSet<Publication> Publications { get; set; } = null!;
    public DbSet<ExternalPublicationIdentity> ExternalIdentities { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=svitlosk;Username=postgres;Password=postgres")
                          .EnableSensitiveDataLogging();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Edition
        modelBuilder.Entity<Edition>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.TargetDate).IsRequired();
            b.Property(e => e.State).IsRequired();
            
            b.HasMany(e => e.Packages)
             .WithOne()
             .HasForeignKey("EditionId")
             .OnDelete(DeleteBehavior.Cascade);
             
            b.Metadata.FindNavigation(nameof(Edition.Packages))!
             .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // PublicationPackage
        modelBuilder.Entity<PublicationPackage>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Name).IsRequired();
            
            b.HasMany(p => p.Publications)
             .WithOne()
             .HasForeignKey("PublicationPackageId")
             .IsRequired()
             .OnDelete(DeleteBehavior.Cascade);
             
            b.Metadata.FindNavigation(nameof(PublicationPackage.Publications))!
             .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // Publication
        modelBuilder.Entity<Publication>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.TerritoryId).IsRequired();
            b.Property(p => p.Classification).IsRequired();
            b.Property(p => p.Type).IsRequired();
            b.Property(p => p.CreatedAt).IsRequired();
            b.Property(p => p.ContentHash).IsRequired();
            b.Property(p => p.HasTomorrowForecast);
            b.Property(p => p.ScheduleHash);
            
            b.Property(p => p.Addresses)
             .HasConversion(
                 v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 v => v == null ? null : JsonSerializer.Deserialize<IReadOnlyList<string>>(v, (JsonSerializerOptions?)null)
             )
             .HasColumnType("jsonb");
             
            b.Property(p => p.PayloadText);
            
            b.Property(p => p.GraphicData)
             .HasConversion(
                 v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 v => v == null ? null : JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(v, (JsonSerializerOptions?)null)
             )
             .HasColumnType("jsonb");
        });

        // ExternalPublicationIdentity
        modelBuilder.Entity<ExternalPublicationIdentity>(b =>
        {
            b.HasKey(e => e.PublicationId);
            b.Property(e => e.ExternalId).IsRequired();
        });

        // OutboxMessage
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(e => e.OperationId);
            b.Property(e => e.PublicationId).IsRequired();
            b.Property(e => e.OperationType).IsRequired();
            b.Property(e => e.ArtifactType).IsRequired();
            b.Property(e => e.Status).IsRequired();
            
            b.HasIndex(e => new { e.Status, e.NextRetryAt, e.CreatedAt });
        });
    }
}
