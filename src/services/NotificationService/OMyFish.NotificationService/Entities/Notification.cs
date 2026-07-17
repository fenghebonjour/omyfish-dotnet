namespace OMyFish.NotificationService.Entities;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Body { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Notification() { }

    public Notification(Guid userId, string type, string title, string? body)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        IsRead = false;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkRead() => IsRead = true;
}
