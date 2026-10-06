using Microsoft.EntityFrameworkCore;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;

namespace Paper.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents", table =>
            {
                table.HasCheckConstraint("CK_Documents_FileSize", "\"FileSize\" >= 0");
                table.HasCheckConstraint("CK_Documents_Title", "length(trim(\"Title\")) > 0");
            });
            entity.Property(document => document.Id).UseIdentityByDefaultColumn();
            entity.Property(document => document.Title).HasMaxLength(300).IsRequired();
            entity.Property(document => document.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(document => document.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(document => document.Hash).HasMaxLength(64).IsRequired();
            entity.Property(document => document.OcrText).HasColumnType("text");
            entity.Property(document => document.OcrStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(document => document.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(document => document.OcrError).HasMaxLength(2000);
            entity.Property(document => document.SearchText).HasColumnType("text").IsRequired();
            entity.HasGeneratedTsVectorColumn(document => document.SearchVector, "simple", document => document.SearchText);
            entity.HasIndex(document => document.SearchVector).HasMethod("GIN");
            entity.HasIndex(document => document.Hash).IsUnique();
            entity.HasIndex(document => new { document.Status, document.UpdatedAt });
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags", table => table.HasCheckConstraint("CK_Tags_Name", "length(trim(\"Name\")) > 0"));
            entity.Property(tag => tag.Id).UseIdentityByDefaultColumn();
            entity.Property(tag => tag.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(tag => tag.Name).IsUnique();
        });

        modelBuilder.Entity<DocumentTag>(entity =>
        {
            entity.ToTable("DocumentTags");
            entity.HasKey(documentTag => new { documentTag.DocumentId, documentTag.TagId });
            entity.HasOne(documentTag => documentTag.Document)
                .WithMany(document => document.Tags)
                .HasForeignKey(documentTag => documentTag.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(documentTag => documentTag.Tag)
                .WithMany(tag => tag.Documents)
                .HasForeignKey(documentTag => documentTag.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.ToTable("ProcessingJobs", table =>
            {
                table.HasCheckConstraint("CK_ProcessingJobs_Attempts", "\"Attempts\" >= 0");
                table.HasCheckConstraint("CK_ProcessingJobs_Priority", "\"Priority\" >= 0");
            });
            entity.Property(job => job.Id).UseIdentityByDefaultColumn();
            entity.Property(job => job.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(job => job.State).HasConversion<string>().HasMaxLength(24);
            entity.Property(job => job.Error).HasMaxLength(2000);
            entity.HasIndex(job => new { job.State, job.Priority, job.CreatedAt });
            entity.HasIndex(job => new { job.DocumentId, job.Type, job.State });
            entity.HasOne(job => job.Document)
                .WithMany()
                .HasForeignKey(job => job.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
