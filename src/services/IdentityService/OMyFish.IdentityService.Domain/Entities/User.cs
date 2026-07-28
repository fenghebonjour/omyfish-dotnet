namespace OMyFish.IdentityService.Domain.Entities;

public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = default!;
    public string HashedPassword { get; private set; } = default!;
    public string? DisplayName { get; private set; }
    public string Role { get; private set; } = "USER";
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static User Create(string email, string hashedPassword, string? displayName = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant().Trim(),
            HashedPassword = hashedPassword,
            DisplayName = displayName,
            Role = "USER",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public void Deactivate() => IsActive = false;
}
