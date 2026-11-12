namespace Miqat.Application.Modules;

/// <summary>What the weekly summary email tells one person.</summary>
public class WeeklySummaryDto
{
    /// <summary>Tasks finished in the last seven days.</summary>
    public int CompletedLastWeek { get; set; }

    /// <summary>Open tasks due in the coming seven days.</summary>
    public int DueThisWeek { get; set; }

    /// <summary>Open tasks whose due date has passed.</summary>
    public int Overdue { get; set; }

    /// <summary>Event dates in the coming seven days.</summary>
    public int EventsThisWeek { get; set; }

    /// <summary>The next few things, soonest first, each as one line of text.</summary>
    public List<string> ComingUp { get; set; } = new();
}
