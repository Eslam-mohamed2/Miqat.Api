using Miqat.Application.Common;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Tests;

/// <summary>
/// The strings the API accepts for enum fields.
///
/// These used to be parsed with a bare Enum.TryParse while the validator held
/// its own hardcoded list, and the two disagreed: "InProgress" passed
/// validation, failed to parse, and left the task's status silently unchanged
/// behind a 204.
/// </summary>
public class EnumTextTests
{
    [Theory]
    [InlineData("Pending", TaskStatus.Pending)]
    [InlineData("In_progress", TaskStatus.In_progress)]
    [InlineData("InProgress", TaskStatus.In_progress)]
    [InlineData("in progress", TaskStatus.In_progress)]
    [InlineData("On_hold", TaskStatus.On_hold)]
    [InlineData("OnHold", TaskStatus.On_hold)]
    [InlineData("completed", TaskStatus.Completed)]
    [InlineData("Cancelled", TaskStatus.Cancelled)]
    public void Task_status_accepts_both_spellings(string text, TaskStatus expected)
    {
        Assert.True(EnumText.TryParseTaskStatus(text, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Sleeping")]
    [InlineData("1")]
    public void Task_status_rejects_anything_else(string? text)
    {
        Assert.False(EnumText.TryParseTaskStatus(text, out _));
    }

    [Fact]
    public void Task_status_is_sent_to_clients_as_the_enum_name()
    {
        Assert.Equal("In_progress", TaskStatus.In_progress.ToString());
        Assert.Equal("On_hold", TaskStatus.On_hold.ToString());
    }

    [Theory]
    [InlineData("High", Priority.High)]
    [InlineData("high", Priority.High)]
    [InlineData(" Critical ", Priority.Critical)]
    public void Defined_names_parse_ignoring_case(string text, Priority expected)
    {
        Assert.True(EnumText.TryParseDefined<Priority>(text, out var priority));
        Assert.Equal(expected, priority);
    }

    [Theory]
    [InlineData("Urgent")]  // not a member
    [InlineData("2")]       // Enum.TryParse would accept a number
    [InlineData("99")]      // ...including one that is not even defined
    [InlineData("-1")]
    [InlineData("")]
    public void Numbers_and_unknown_names_are_rejected(string text)
    {
        Assert.False(EnumText.TryParseDefined<Priority>(text, out _));
    }

    [Fact]
    public void Gender_other_is_accepted()
    {
        Assert.True(EnumText.TryParseDefined<Gender>("Other", out var gender));
        Assert.Equal(Gender.Other, gender);
    }
}
