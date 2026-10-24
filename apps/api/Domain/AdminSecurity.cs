namespace RabbitHole.Domain;

public sealed class AdminAccount
{
    public int Id { get; set; } = 1;
    public string PasswordHash { get; set; } = "";
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset PasswordChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AdminSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int AccountId { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
