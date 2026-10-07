using Microsoft.EntityFrameworkCore;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Storage;

namespace Paper.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<MailImportState> MailImportStates => Set<MailImportState>();

    public DbSet<MailImportFailure> MailImportFailures => Set<MailImportFailure>();

    public DbSet<AnalysisRule> AnalysisRules => Set<AnalysisRule>();

    public DbSet<Correspondent> Correspondents => Set<Correspondent>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<ShelfFolder> ShelfFolders => Set<ShelfFolder>();

    public DbSet<CustomField> CustomFields => Set<CustomField>();

    public DbSet<DocumentCustomFieldValue> DocumentCustomFieldValues => Set<DocumentCustomFieldValue>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeShelfPathKeys();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeShelfPathKeys();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isPostgres = string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents", table =>
            {
                table.HasCheckConstraint("CK_Documents_FileSize", "\"FileSize\" >= 0");
                table.HasCheckConstraint("CK_Documents_Title", "length(trim(\"Title\")) > 0");
                table.HasCheckConstraint("CK_Documents_Status", "\"Status\" IN ('Inbox', 'Filed', 'Deferred', 'Ignored')");
                table.HasCheckConstraint("CK_Documents_OcrStatus", "\"OcrStatus\" IN ('Pending', 'Processing', 'Completed', 'Failed')");
            });
            if (isPostgres)
            {
                entity.Property(document => document.Id).UseIdentityByDefaultColumn();
            }
            entity.Property(document => document.Title).HasMaxLength(300).IsRequired();
            entity.Property(document => document.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(document => document.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(document => document.Hash).HasMaxLength(64).IsRequired();
            entity.Property(document => document.OcrText).HasColumnType("text");
            entity.Property(document => document.OcrStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(document => document.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(document => document.OcrError).HasMaxLength(2000);
            entity.Property(document => document.SearchText).HasColumnType("text").IsRequired();
            if (isPostgres)
            {
                entity.HasGeneratedTsVectorColumn(document => document.SearchVector, "german", document => document.SearchText);
                entity.HasIndex(document => document.SearchVector).HasMethod("GIN");
            }
            else
            {
                entity.Ignore(document => document.SearchVector);
            }
            entity.HasIndex(document => document.Hash).IsUnique();
            entity.HasIndex(document => new { document.Status, document.UpdatedAt });
            entity.HasIndex(document => document.CorrespondentId);
            entity.HasIndex(document => document.DocumentTypeId);
            entity.HasIndex(document => document.ShelfFolderId);
            entity.HasIndex(document => document.SuggestedShelfFolderId);
            entity.HasOne(document => document.Correspondent)
                .WithMany(correspondent => correspondent.Documents)
                .HasForeignKey(document => document.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(document => document.DocumentType)
                .WithMany(documentType => documentType.Documents)
                .HasForeignKey(document => document.DocumentTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(document => document.ShelfFolder)
                .WithMany(folder => folder.Documents)
                .HasForeignKey(document => document.ShelfFolderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(document => document.SuggestedShelfFolder)
                .WithMany()
                .HasForeignKey(document => document.SuggestedShelfFolderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Correspondent>(entity =>
        {
            entity.ToTable("Correspondents", table => table.HasCheckConstraint("CK_Correspondents_Name", "length(trim(\"Name\")) > 0"));
            if (isPostgres)
            {
                entity.Property(correspondent => correspondent.Id).UseIdentityByDefaultColumn();
            }
            entity.Property(correspondent => correspondent.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(correspondent => correspondent.Name).IsUnique();
        });

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.ToTable("DocumentTypes", table => table.HasCheckConstraint("CK_DocumentTypes_Name", "length(trim(\"Name\")) > 0"));
            if (isPostgres)
            {
                entity.Property(documentType => documentType.Id).UseIdentityByDefaultColumn();
            }
            entity.Property(documentType => documentType.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(documentType => documentType.Name).IsUnique();
        });

        modelBuilder.Entity<ShelfFolder>(entity =>
        {
            entity.ToTable("ShelfFolders", table =>
            {
                table.HasCheckConstraint("CK_ShelfFolders_Name", "length(trim(\"Name\")) > 0");
                table.HasCheckConstraint("CK_ShelfFolders_RelativePath", "length(trim(\"RelativePath\")) > 0");
            });
            if (isPostgres)
            {
                entity.Property(folder => folder.Id).UseIdentityByDefaultColumn();
            }
            entity.Property(folder => folder.Name).HasMaxLength(120).IsRequired();
            entity.Property(folder => folder.RelativePath).HasMaxLength(500).IsRequired();
            entity.Property(folder => folder.RelativePathKey).HasMaxLength(500).IsRequired();
            entity.HasIndex(folder => folder.RelativePath);
            entity.HasIndex(folder => folder.RelativePathKey).IsUnique();
            entity.HasIndex(folder => new { folder.ParentId, folder.Name }).IsUnique();
            entity.HasOne(folder => folder.Parent)
                .WithMany(parent => parent.Children)
                .HasForeignKey(folder => folder.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomField>(entity =>
        {
            entity.ToTable("CustomFields", table =>
            {
                table.HasCheckConstraint("CK_CustomFields_Name", "length(trim(\"Name\")) > 0");
                table.HasCheckConstraint("CK_CustomFields_Type", "\"Type\" IN ('Text', 'Number', 'Date', 'Boolean')");
            });
            if (isPostgres)
            {
                entity.Property(field => field.Id).UseIdentityByDefaultColumn();
            }
            entity.Property(field => field.Name).HasMaxLength(120).IsRequired();
            entity.Property(field => field.Type).HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(field => field.Name).IsUnique();
        });

        modelBuilder.Entity<DocumentCustomFieldValue>(entity =>
        {
            entity.ToTable("DocumentCustomFieldValues");
            entity.HasKey(value => new { value.DocumentId, value.CustomFieldId });
            entity.HasIndex(value => new { value.CustomFieldId, value.DocumentId });
            entity.Property(value => value.Value).HasMaxLength(2000).IsRequired();
            entity.HasOne(value => value.Document)
                .WithMany(document => document.CustomFields)
                .HasForeignKey(value => value.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.CustomField)
                .WithMany(field => field.Values)
                .HasForeignKey(value => value.CustomFieldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags", table => table.HasCheckConstraint("CK_Tags_Name", "length(trim(\"Name\")) > 0"));
            if (isPostgres)
            {
                entity.Property(tag => tag.Id).UseIdentityByDefaultColumn();
            }
            entity.Property(tag => tag.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(tag => tag.Name).IsUnique();
        });

        modelBuilder.Entity<DocumentTag>(entity =>
        {
            entity.ToTable("DocumentTags");
            entity.HasKey(documentTag => new { documentTag.DocumentId, documentTag.TagId });
            entity.HasIndex(documentTag => new { documentTag.TagId, documentTag.DocumentId });
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
                table.HasCheckConstraint("CK_ProcessingJobs_Type", "\"Type\" IN ('OcrAndAnalyze')");
                table.HasCheckConstraint("CK_ProcessingJobs_State", "\"State\" IN ('Pending', 'Running', 'Succeeded', 'Failed')");
            });
            if (isPostgres)
            {
                entity.Property(job => job.Id).UseIdentityByDefaultColumn();
            }
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

        modelBuilder.Entity<MailImportState>(entity =>
        {
            entity.ToTable("MailImportStates", table =>
                table.HasCheckConstraint("CK_MailImportStates_LastUid", "\"LastUid\" >= 0"));
            entity.Property(state => state.AccountName).HasMaxLength(120).IsRequired();
            entity.Property(state => state.LastError).HasMaxLength(2000);
            entity.HasIndex(state => state.AccountName).IsUnique();
        });

        modelBuilder.Entity<MailImportFailure>(entity =>
        {
            entity.ToTable("MailImportFailures", table =>
                table.HasCheckConstraint("CK_MailImportFailures_Uid", "\"Uid\" >= 0"));
            entity.Property(failure => failure.AccountName).HasMaxLength(120).IsRequired();
            entity.Property(failure => failure.Error).HasMaxLength(2000).IsRequired();
            entity.HasIndex(failure => new { failure.AccountName, failure.CreatedAt });
        });

        modelBuilder.Entity<AnalysisRule>(entity =>
        {
            entity.ToTable("AnalysisRules", table =>
            {
                table.HasCheckConstraint("CK_AnalysisRules_UseCount", "\"UseCount\" > 0");
                table.HasCheckConstraint("CK_AnalysisRules_Target", "\"CorrespondentId\" IS NOT NULL OR \"DocumentTypeId\" IS NOT NULL OR \"ShelfFolderId\" IS NOT NULL");
            });
            entity.Property(rule => rule.Term).HasMaxLength(80).IsRequired();
            entity.HasIndex(rule => new { rule.Term, rule.UseCount });
            entity.HasOne<Correspondent>().WithMany().HasForeignKey(rule => rule.CorrespondentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentType>().WithMany().HasForeignKey(rule => rule.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ShelfFolder>().WithMany().HasForeignKey(rule => rule.ShelfFolderId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void NormalizeShelfPathKeys()
    {
        foreach (var entry in ChangeTracker.Entries<ShelfFolder>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.RelativePathKey = StoragePathPolicy.CreatePathKey(entry.Entity.RelativePath);
        }
    }
}
