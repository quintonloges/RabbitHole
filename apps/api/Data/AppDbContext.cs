using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RabbitHole.Domain;
using System.Text.Json;

namespace RabbitHole.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<ContentItem> Items => Set<ContentItem>();
    public DbSet<ItemIdentity> ItemIdentities => Set<ItemIdentity>();
    public DbSet<ItemSource> ItemSources => Set<ItemSource>();
    public DbSet<ItemTopic> ItemTopics => Set<ItemTopic>();
    public DbSet<ItemState> States => Set<ItemState>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<LibraryDocument> Documents => Set<LibraryDocument>();
    public DbSet<DocumentSection> Sections => Set<DocumentSection>();
    public DbSet<RabbitHole.Domain.RabbitHole> Holes => Set<RabbitHole.Domain.RabbitHole>();
    public DbSet<RabbitNode> Nodes => Set<RabbitNode>();
    public DbSet<PracticePrompt> Prompts => Set<PracticePrompt>();
    public DbSet<PracticeAttempt> Attempts => Set<PracticeAttempt>();
    public DbSet<FeedEdition> Editions => Set<FeedEdition>();
    public DbSet<FeedEntry> Entries => Set<FeedEntry>();
    public DbSet<IngestionJob> Jobs => Set<IngestionJob>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<AiResult> AiResults => Set<AiResult>();
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ItemState>().HasKey(x => x.ItemId);
        builder.Entity<AppSetting>().HasKey(x => x.Key);
        builder.Entity<AdminAccount>().ToTable("AdminAccounts", table => table.HasCheckConstraint("CK_AdminAccounts_SingleAccount", "\"Id\" = 1"));
        builder.Entity<AdminAccount>().Property(x => x.Id).ValueGeneratedNever();
        builder.Entity<AdminSession>().HasOne<AdminAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AdminSession>().HasIndex(x => x.ExpiresAt);
        builder.Entity<ItemIdentity>().HasKey(x => x.Key);
        builder.Entity<ItemTopic>().HasKey(x => new { x.ItemId, x.TopicId });
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(List<string>)))
            {
                property.SetValueConverter(new ValueConverter<List<string>, string>(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>()));
                property.SetValueComparer(new ValueComparer<List<string>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, (hash, text) => HashCode.Combine(hash, text)),
                    value => value.ToList()));
            }
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(List<Guid>)))
            {
                var converter = new ValueConverter<List<Guid>, string>(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => JsonSerializer.Deserialize<List<Guid>>(value, (JsonSerializerOptions?)null) ?? new List<Guid>());
                var comparer = new ValueComparer<List<Guid>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, (hash, guid) => HashCode.Combine(hash, guid)),
                    value => value.ToList());
                property.SetValueConverter(converter);
                property.SetValueComparer(comparer);
            }
        }
        builder.Entity<Topic>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<Source>().HasIndex(x => x.NextSyncAt);
        builder.Entity<ContentItem>().HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ContentItem>().HasOne<LibraryDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ContentItem>().HasQueryFilter(x => x.MergedIntoId == null);
        builder.Entity<ContentItem>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ContentItem>().HasIndex(x => x.CanonicalUrl);
        builder.Entity<ContentItem>().HasIndex(x => new { x.Provider, x.ProviderId });
        builder.Entity<ContentItem>().HasIndex(x => x.Doi);
        builder.Entity<ContentItem>().HasIndex(x => x.ArxivId);
        builder.Entity<ItemIdentity>().Property(x => x.Key).HasMaxLength(64);
        builder.Entity<ItemIdentity>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ItemSource>().HasIndex(x => new { x.ItemId, x.SourceKey, x.SourceVersion }).IsUnique();
        builder.Entity<ItemSource>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ItemSource>().HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ItemTopic>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ItemTopic>().HasOne<Topic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentItem>().HasIndex(x => x.PublishedAt);
        builder.Entity<ContentItem>().HasIndex(x => x.SourceId);
        builder.Entity<ContentItem>().HasIndex(x => x.DocumentId);
        builder.Entity<ItemState>().HasOne<ContentItem>().WithOne().HasForeignKey<ItemState>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Note>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Note>().HasIndex(x => x.ItemId);
        builder.Entity<LibraryDocument>().HasIndex(x => x.Sha256).IsUnique();
        builder.Entity<DocumentSection>().HasOne<LibraryDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<DocumentSection>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DocumentSection>().HasIndex(x => x.DocumentId);
        builder.Entity<RabbitNode>().HasOne<RabbitHole.Domain.RabbitHole>().WithMany().HasForeignKey(x => x.HoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<RabbitNode>().HasOne<RabbitNode>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RabbitNode>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RabbitNode>().HasIndex(x => x.HoleId);
        builder.Entity<RabbitNode>().HasIndex(x => x.ItemId);
        builder.Entity<PracticePrompt>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PracticePrompt>().HasIndex(x => x.ItemId);
        builder.Entity<PracticePrompt>().HasIndex(x => x.DueAt);
        builder.Entity<PracticeAttempt>().HasOne<PracticePrompt>().WithMany().HasForeignKey(x => x.PromptId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PracticeAttempt>().HasIndex(x => x.PromptId);
        builder.Entity<FeedEdition>().HasIndex(x => x.Day).IsUnique();
        builder.Entity<FeedEntry>().HasOne<FeedEdition>().WithMany().HasForeignKey(x => x.EditionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<FeedEntry>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FeedEntry>().HasIndex(x => new { x.EditionId, x.ItemId }).IsUnique();
        builder.Entity<FeedEntry>().HasIndex(x => new { x.EditionId, x.Position }).IsUnique();
        builder.Entity<IngestionJob>().HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<IngestionJob>().HasIndex(x => x.SourceId);
        builder.Entity<AiResult>().HasOne<ContentItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AiResult>().HasIndex(x => x.ItemId);
    }
}
