using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure;

public class KnowledgeHubDbContext(DbContextOptions<KnowledgeHubDbContext> options) : DbContext(options)
{
    public DbSet<CompanyDocument> Documents => Set<CompanyDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyDocument>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(260);
            e.Property(d => d.Department).HasMaxLength(50);
            e.HasIndex(d => d.Department);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(d => d.Chunks).WithOne(c => c.Document)
                .HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.Property(c => c.Embedding).HasColumnType("vector(1536)");
            e.HasIndex(c => new { c.DocumentId, c.SequenceNumber }).IsUnique();
        });

        modelBuilder.Entity<OutboxEmail>(e =>
        {
            e.Property(m => m.To).HasMaxLength(320);
            e.Property(m => m.Subject).HasMaxLength(500);
            e.Property(m => m.Department).HasMaxLength(50);
            e.Property(m => m.RequestedBy).HasMaxLength(100);
        });
    }
}
