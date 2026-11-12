namespace Miqat.Application.Modules;

public class BoardDto
{
    public Guid Id { get; set; }
    /// <summary>"Whiteboard" or "NodeFlow".</summary>
    public string Kind { get; set; } = "Whiteboard";
    public string Name { get; set; } = string.Empty;
    /// <summary>Client-owned JSON. The server never inspects it.</summary>
    public string Content { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create/update payload. Name is optional; the entity supplies a default.</summary>
public class SaveBoardDto
{
    public string Kind { get; set; } = "Whiteboard";
    public string? Name { get; set; }
    public string Content { get; set; } = "{}";
}
