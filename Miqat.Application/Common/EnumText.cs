using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Common;

/// <summary>
/// Converts between enums and the strings used on the wire.
/// The TaskStatus enum is stored as "In_progress" / "On_hold", while clients
/// send and expect "InProgress" / "OnHold", so both spellings are accepted
/// and the client-friendly one is returned.
/// </summary>
public static class EnumText
{
    /// <summary>Parses a defined enum name, ignoring case. Rejects numbers and unknown names.</summary>
    public static bool TryParseDefined<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        if (char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '+') return false;

        return Enum.TryParse(trimmed, ignoreCase: true, out result) && Enum.IsDefined(result);
    }

    public static bool TryParseTaskStatus(string? value, out TaskStatus status)
    {
        status = TaskStatus.Pending;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant();
        switch (normalized)
        {
            case "pending": status = TaskStatus.Pending; return true;
            case "inprogress": status = TaskStatus.In_progress; return true;
            case "completed": status = TaskStatus.Completed; return true;
            case "cancelled":
            case "canceled": status = TaskStatus.Cancelled; return true;
            case "onhold": status = TaskStatus.On_hold; return true;
            default: return false;
        }
    }

    public static string ToText(TaskStatus status) => status switch
    {
        TaskStatus.In_progress => "InProgress",
        TaskStatus.On_hold => "OnHold",
        _ => status.ToString()
    };
}
