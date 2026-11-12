using Miqat.Domain.Common;

namespace Miqat.Domain.Entities;

/// <summary>
/// One of the calendars of a user ("Personal", "Work", ...). An event lives in
/// exactly one calendar, and is drawn in the colour of that calendar.
/// <para>
/// Named UserCalendar because System.Globalization already has a Calendar.
/// </para>
/// </summary>
public class UserCalendar : BaseEntity
{
    public const string DefaultName = "Personal";
    public const string DefaultColor = "#4F46E5";

    public string Name { get; private set; } = DefaultName;
    public string Color { get; private set; } = DefaultColor;

    /// <summary>Every user has exactly one. It cannot be deleted; events of a deleted calendar move here.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Hidden calendars stay out of the calendar feed.</summary>
    public bool IsVisible { get; set; } = true;

    public Guid OwnerId { get; private set; }
    public virtual User Owner { get; private set; } = null!;

    public UserCalendar(string name, string? color, Guid ownerId, bool isDefault = false)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("OwnerId cannot be empty.", nameof(ownerId));

        OwnerId = ownerId;
        IsDefault = isDefault;
        Update(name, color);
    }

    private UserCalendar() { }

    public static UserCalendar CreateDefault(Guid ownerId) =>
        new(DefaultName, DefaultColor, ownerId, isDefault: true);

    /// <summary>Blank values keep what is already stored.</summary>
    public void Update(string? name, string? color)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(color)) Color = color.Trim();
    }
}
