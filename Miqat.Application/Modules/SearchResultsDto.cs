namespace Miqat.Application.Modules;

/// <summary>Result of GET /api/Search - one list per kind of thing.</summary>
public class SearchResultsDto
{
    public string Query { get; set; } = string.Empty;
    public List<TaskDto> Tasks { get; set; } = new();
    public List<SearchProjectDto> Projects { get; set; } = new();
    public List<UserDto> Users { get; set; } = new();
    public List<SearchEventDto> Events { get; set; } = new();
}

public class SearchEventDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsRecurring { get; set; }
    public bool IsOwner { get; set; }
}

public class SearchProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public Guid OwnerId { get; set; }
}
