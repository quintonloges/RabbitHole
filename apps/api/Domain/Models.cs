namespace RabbitHole.Domain;

public class Topic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Aliases { get; set; } = "";
    public int Weight { get; set; } = 3;
    public bool Pinned { get; set; }
    public bool CuriosityOnly { get; set; }
    public bool Enabled { get; set; } = true;
}

public class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "rss";
    public string Url { get; set; } = "";
    public string Query { get; set; } = "";
    public List<Guid> TopicIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 360;
    public int ResultLimit { get; set; } = 25;
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset NextSyncAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public int FailureCount { get; set; }
    public int LastImportedCount { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
}

public class ContentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SourceId { get; set; }
    public string Kind { get; set; } = "article";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string CanonicalUrl { get; set; } = "";
    public string Provider { get; set; } = "manual";
    public string? ProviderId { get; set; }
    public string? Doi { get; set; }
    public string? ArxivId { get; set; }
    public string? Excerpt { get; set; }
    public string? Body { get; set; }
    public string TextProvenance { get; set; } = "metadata";
    public string? ImageUrl { get; set; }
    public string? EmbedUrl { get; set; }
    public string? MetadataJson { get; set; }
    public string? ImportWarning { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Guid> TopicIds { get; set; } = [];
    public string ClassificationOrigin { get; set; } = "source-keywords";
    public Guid? DocumentId { get; set; }
    public Guid? SectionId { get; set; }
}

public class ItemState
{
    public Guid ItemId { get; set; }
    public bool Saved { get; set; }
    public DateTimeOffset? SavedAt { get; set; }
    public bool Read { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool Dismissed { get; set; }
    public bool Understood { get; set; }
    public double Progress { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public int Feedback { get; set; }
}

public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public string Body { get; set; } = "";
    public string? Quote { get; set; }
    public string? Locator { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class LibraryDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string FileName { get; set; } = "";
    public string StorageName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public int PageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class DocumentSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid ItemId { get; set; }
    public string Title { get; set; } = "";
    public int Order { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public string Text { get; set; } = "";
    public string? Locator { get; set; }
}

public class RabbitHole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public Guid? LastNodeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RabbitNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HoleId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? ItemId { get; set; }
    public string? Question { get; set; }
    public string Rationale { get; set; } = "Chosen by you";
    public List<Guid> EvidenceItemIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PracticePrompt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public string Question { get; set; } = "";
    public string? Reference { get; set; }
    public string Kind { get; set; } = "reflection";
    public string Provenance { get; set; } = "deterministic";
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow;
    public double IntervalDays { get; set; }
    public double Ease { get; set; } = 2.5;
    public int Reviews { get; set; }
}

public class PracticeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromptId { get; set; }
    public string Response { get; set; } = "";
    public string Rating { get; set; } = "skip";
    public string? AiFeedback { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class FeedEdition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Day { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class FeedEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EditionId { get; set; }
    public Guid ItemId { get; set; }
    public int Position { get; set; }
    public string Reason { get; set; } = "";
    public List<Guid> EvidenceItemIds { get; set; } = [];
}

public class IngestionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public string Status { get; set; } = "running";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public int Imported { get; set; }
    public string? Error { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class AiResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public string Action { get; set; } = "summary";
    public string Text { get; set; } = "";
    public List<Guid> EvidenceItemIds { get; set; } = [];
    public string Model { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
