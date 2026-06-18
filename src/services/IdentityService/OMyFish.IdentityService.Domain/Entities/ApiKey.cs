namespace OMyFish.IdentityService.Domain.Entities;

public sealed class ApiKey
{
    private ApiKey() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string KeyHash { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }

    public static ApiKey Create(Guid userId, string keyHash, string name, DateTime? expiresAt = null)
    {
        return new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            KeyHash = keyHash,
            Name = name,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
    }

    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
