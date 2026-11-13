namespace Miqat.API.Swagger;

/// <summary>
/// Made-up values for the Swagger examples: a new person, a task, a project.
///
/// A fresh set is drawn every time the document is generated, so "register"
/// offers a new email address on every page load instead of colliding with
/// the account created on the previous try.
/// </summary>
public sealed class FakeData
{
    private static readonly string[] FirstNames =
        { "Sara", "Omar", "Layla", "Youssef", "Nour", "Karim", "Mona", "Hassan", "Dina", "Tarek" };

    private static readonly string[] LastNames =
        { "Ali", "Mansour", "Farouk", "Saleh", "Nasser", "Hamdy", "Aziz", "Khalil", "Samir", "Fahmy" };

    private static readonly (string Country, string TimeZone, string DialCode)[] Places =
    {
        ("Egypt", "Africa/Cairo", "+20"),
        ("Saudi Arabia", "Asia/Riyadh", "+966"),
        ("United Arab Emirates", "Asia/Dubai", "+971"),
        ("Jordan", "Asia/Amman", "+962"),
        ("United Kingdom", "Europe/London", "+44")
    };

    private static readonly (string Title, string Description, string Tags)[] Tasks =
    {
        ("Prepare the sprint demo", "Collect the finished stories and rehearse the walkthrough.", "demo,sprint"),
        ("Review the onboarding flow", "Go through sign-up on a phone and note every rough edge.", "ux,review"),
        ("Write the release notes", "Summarise what changed for customers in plain language.", "release,docs"),
        ("Fix the calendar time zone bug", "Events created near midnight land on the wrong day.", "bug,calendar"),
        ("Plan next month's content", "Draft topics and owners for the four weekly posts.", "planning,content")
    };

    private static readonly (string Name, string Description)[] Projects =
    {
        ("Website Relaunch", "Everything needed to ship the new marketing site."),
        ("Mobile App", "Tasks for the iOS and Android releases."),
        ("Customer Research", "Interviews, surveys and what we learn from them."),
        ("Quarterly Planning", "Goals and milestones for the coming quarter.")
    };

    private static readonly (string Title, string Description, string Location)[] Events =
    {
        ("Project sync", "Where each part stands and what is in the way.", "Meeting room 2"),
        ("Design review", "Walk through the new screens together.", "Online"),
        ("Lunch with the team", "No agenda.", "The cafe downstairs"),
        ("Customer call", "Feedback on the last release.", "Online")
    };

    private static readonly string[] Calendars = { "Family", "Fitness", "Study", "Travel", "Side project" };

    private static readonly string[] ChecklistLines =
        { "Write the first draft", "Ask for feedback", "Book the room", "Send the summary" };

    private static readonly string[] Colors =
        { "#2EC4A0", "#FF6B6B", "#4D96FF", "#FFD93D", "#9B5DE5", "#F4A261" };

    private static readonly string[] Comments =
    {
        "Looks good to me. Can we ship this on Thursday?",
        "I pushed an update, please take another look.",
        "Blocked until the design is approved.",
        "Done on my side. Moving on to the next one."
    };

    private static readonly string[] ChatLines =
    {
        "Are we still on for the review at three?",
        "I sent you the file, have a look when you can.",
        "Running five minutes late, start without me.",
        "Thanks, that fixed it!"
    };

    private static readonly string[] ChatGroupNames =
        { "Weekend plans", "Launch crew", "Study group", "Family" };

    private static readonly string[] Bios =
    {
        "Product designer who likes tidy calendars.",
        "Backend developer, coffee first.",
        "Project manager keeping the team on schedule."
    };

    private readonly Random _random;

    public FakeData(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();

        var place = Pick(Places);
        var first = Pick(FirstNames);
        var last = Pick(LastNames);

        // The number keeps the address unique between page loads.
        var email = $"{first}.{last}.{_random.Next(1000, 99999)}@example.com".ToLowerInvariant();

        Person = new SamplePerson(
            $"{first} {last}",
            email,
            $"{place.DialCode}{_random.Next(100000000, 999999999)}",
            place.Country,
            place.TimeZone,
            _random.Next(2) == 0 ? "Female" : "Male",
            new DateTime(_random.Next(1985, 2003), _random.Next(1, 13), _random.Next(1, 28), 0, 0, 0, DateTimeKind.Utc),
            Pick(Bios));

        var task = Pick(Tasks);
        TaskTitle = task.Title;
        TaskDescription = task.Description;
        TaskTags = task.Tags;

        var project = Pick(Projects);
        ProjectName = project.Name;
        ProjectDescription = project.Description;

        Color = Pick(Colors);
        Comment = Pick(Comments);
        Bio = Pick(Bios);
        BoardName = $"{project.Name} board";

        // Not the address of the new person above: "register" may already have used that one.
        NewEmail = $"{first}.{last}.new{_random.Next(1000, 99999)}@example.com".ToLowerInvariant();

        var calendarEvent = Pick(Events);
        EventTitle = calendarEvent.Title;
        EventDescription = calendarEvent.Description;
        EventLocation = calendarEvent.Location;
        CalendarName = Pick(Calendars);
        ChecklistLine = Pick(ChecklistLines);
        ChatLine = Pick(ChatLines);
        ChatGroupName = Pick(ChatGroupNames);
    }

    /// <summary>A person who does not have an account yet.</summary>
    public SamplePerson Person { get; }

    /// <summary>Meets the password rules: 8+ characters, upper case, digit, symbol.</summary>
    public string Password => "Passw0rd!";

    public string TaskTitle { get; }
    public string TaskDescription { get; }
    public string TaskTags { get; }
    public string ProjectName { get; }
    public string ProjectDescription { get; }
    public string Color { get; }
    public string Comment { get; }
    public string Bio { get; }
    public string BoardName { get; }

    /// <summary>An address nobody has: what an account changes its sign-in email to.</summary>
    public string NewEmail { get; }

    public string EventTitle { get; }
    public string EventDescription { get; }
    public string EventLocation { get; }
    public string CalendarName { get; }
    public string ChecklistLine { get; }
    public string ChatLine { get; }
    public string ChatGroupName { get; }

    /// <summary>Boards store their drawing as a JSON document in a string.</summary>
    public string BoardContent =>
        "{\"nodes\":[{\"id\":\"1\",\"label\":\"Idea\",\"x\":120,\"y\":80},{\"id\":\"2\",\"label\":\"Plan\",\"x\":320,\"y\":80}]," +
        "\"edges\":[{\"from\":\"1\",\"to\":\"2\"}]}";

    private T Pick<T>(T[] values) => values[_random.Next(values.Length)];
}
