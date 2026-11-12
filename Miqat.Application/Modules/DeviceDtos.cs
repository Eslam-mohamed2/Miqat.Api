namespace Miqat.Application.Modules;

public class DeviceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// The push subscription a browser hands out (PushManager.subscribe):
/// its endpoint and the two keys under "keys".
/// </summary>
public class RegisterDeviceDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;

    /// <summary>A label the user recognises, for example "Chrome on Windows".</summary>
    public string? Name { get; set; }
}

/// <summary>What a browser needs to subscribe: the public key of this server.</summary>
public class PushKeyDto
{
    /// <summary>False when the server has no push keys; notifications then only arrive while the app is open.</summary>
    public bool IsConfigured { get; set; }
    public string? PublicKey { get; set; }
}
