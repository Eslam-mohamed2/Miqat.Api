namespace Miqat.Domain.Enumerations;

/// <summary>How a voice or video call ended, as the chat remembers it.</summary>
public enum CallOutcome
{
    /// <summary>It was answered and the two talked.</summary>
    Completed,

    /// <summary>Nobody answered: the caller gave up, it rang out, or the friend was offline or on another call.</summary>
    Missed,

    /// <summary>The friend said no.</summary>
    Declined
}
