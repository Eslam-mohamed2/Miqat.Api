using Miqat.Domain.Common;

namespace Miqat.Domain.Entities;

/// <summary>
/// A browser (or installed web app) that asked to receive notifications while
/// Miqat is closed. The three values come from the push subscription the
/// browser creates.
/// </summary>
public class Device : BaseEntity
{
    public Guid UserId { get; private set; }
    public virtual User User { get; private set; } = null!;

    /// <summary>A label the user recognises, for example "Chrome on Windows".</summary>
    public string Name { get; private set; } = string.Empty;

    public string Endpoint { get; private set; } = string.Empty;
    public string P256dh { get; private set; } = string.Empty;
    public string Auth { get; private set; } = string.Empty;

    public DateTime LastSeenAt { get; private set; } = DateTime.UtcNow;

    public Device(Guid userId, string endpoint, string p256dh, string auth, string? name)
    {
        Endpoint = endpoint;
        Register(userId, p256dh, auth, name);
    }

    private Device() { }

    /// <summary>The same browser registering again, possibly for another account.</summary>
    public void Register(Guid userId, string p256dh, string auth, string? name)
    {
        UserId = userId;
        P256dh = p256dh;
        Auth = auth;
        Name = string.IsNullOrWhiteSpace(name) ? "Browser" : name.Trim();
        LastSeenAt = DateTime.UtcNow;
    }
}
