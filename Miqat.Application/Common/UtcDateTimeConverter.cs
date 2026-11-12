using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miqat.Application.Common;

/// <summary>
/// PostgreSQL "timestamp with time zone" columns only accept UTC values, and
/// Npgsql throws for DateTimeKind.Unspecified. Clients send dates in several
/// shapes ("2026-01-01", "2026-01-01T10:00", "...Z", "...+02:00"), so every
/// incoming date is normalised to UTC here. Values without an offset are
/// treated as UTC.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Parse(reader.GetString()) ?? throw new JsonException("A date value is required.");

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToUtc(value));

    internal static DateTime? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
            throw new JsonException($"\"{text}\" is not a valid date.");

        return parsed.UtcDateTime;
    }

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// The same rule for dates that do not come through JSON (query strings):
    /// a value without a zone is taken as UTC.
    /// </summary>
    public static DateTime? Normalize(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;
}

/// <summary>Same as <see cref="UtcDateTimeConverter"/>; an empty string is read as null.</summary>
public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : UtcDateTimeConverter.Parse(reader.GetString());

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(UtcDateTimeConverter.ToUtc(value.Value));
    }
}
