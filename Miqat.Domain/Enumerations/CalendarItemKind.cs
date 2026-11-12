namespace Miqat.Domain.Enumerations;

/// <summary>
/// The two things that sit on a calendar. Reminders and attachments can hang
/// off either one, so they store this next to the id of the item.
/// </summary>
public enum CalendarItemKind
{
    Task,
    Event
}
