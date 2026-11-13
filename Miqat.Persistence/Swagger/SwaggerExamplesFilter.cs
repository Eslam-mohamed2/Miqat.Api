using System.Globalization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Miqat.API.Swagger;

/// <summary>
/// Fills every endpoint in the Swagger document with example data, so
/// "Try it out" works without typing: ids in the address, query values and a
/// complete request body.
///
/// Nothing here is written per endpoint. A value is chosen from the NAME of
/// the field (email, title, dueDate, groupId ...), so an endpoint or a field
/// added later gets an example without touching this file. The few places
/// where the same name means different things (whose email, which user id)
/// are decided from the address of the endpoint.
///
/// Ids come from <see cref="SwaggerSampleProvider"/>: real rows of the demo
/// account when demo data is on, made-up ids otherwise.
/// </summary>
public sealed class SwaggerExamplesFilter : IDocumentFilter
{
    /// <summary>
    /// Shown as the refresh token in the examples. The script on the Swagger
    /// page swaps it for the token of the last sign-in (see SwaggerUiScripts).
    /// </summary>
    public const string RefreshTokenPlaceholder = "filled-in-automatically-after-sign-in";

    /// <summary>Marks an id example that is not backed by a real row.</summary>
    public const string MissingSampleExtension = "x-sample-missing";

    private const string OtpPlaceholder = "123456";
    private static readonly Guid MadeUpId = Guid.Parse("00000000-0000-4000-8000-000000000001");

    private readonly SwaggerSampleProvider _provider;

    public SwaggerExamplesFilter(SwaggerSampleProvider provider) => _provider = provider;

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var samples = _provider.Resolve();
        var fake = new FakeData();

        swaggerDoc.Info.Description = Intro(samples);

        foreach (var (path, item) in swaggerDoc.Paths)
        {
            foreach (var (method, operation) in item.Operations)
            {
                var call = new Call(path, method, samples, fake);

                ApplyParameters(operation, call);
                ApplyBody(swaggerDoc, operation, call);
                ApplyNotes(operation, call);
            }
        }
    }

    // ── Address and query values ────────────────────────────────────────────

    private static void ApplyParameters(OpenApiOperation operation, Call call)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In == ParameterLocation.Path)
            {
                // The one address value that is a word, not an id: /api/Task/... or /api/Event/...
                if (parameter.Name.Equals("itemKind", StringComparison.OrdinalIgnoreCase))
                {
                    parameter.Example = new OpenApiString("Task");
                    continue;
                }

                var (id, missing) = PathId(call, parameter.Name);
                parameter.Example = new OpenApiString((id ?? MadeUpId).ToString());

                if (id == null && call.Samples.IsLive)
                {
                    parameter.Extensions[MissingSampleExtension] = new OpenApiBoolean(true);
                    call.Notes.Add(missing);
                }
            }
            else if (parameter.In == ParameterLocation.Query)
            {
                ApplyQuery(parameter, call);
            }
        }
    }

    private static (Guid? Id, string Missing) PathId(Call call, string name)
    {
        var s = call.Samples;

        switch (name.ToLowerInvariant())
        {
            case "id":
                return call.Area switch
                {
                    "Board" => (s.BoardId, "The demo account has no board yet. Create one with POST /api/Board, then reload this page."),
                    "Group" => (s.GroupId, "The demo account owns no project. Create one with POST /api/Group, then reload this page."),
                    "Task" => (s.TaskId, "The demo account has no task. Create one with POST /api/Task, then reload this page."),
                    "Notification" => (s.NotificationId, "The demo account has no notifications right now."),
                    "Calendar" => (s.CalendarId, "The demo account has only its default calendar. Create another with POST /api/Calendar, then reload this page."),
                    "Event" => EventId(call),
                    "Reminder" => (s.ReminderId, "The demo account has set no reminder. Add one with POST /api/Event/{itemId}/reminders, then reload this page."),
                    "Attachment" => (s.AttachmentId, "The demo account has attached no file. Attach one with POST /api/Task/{itemId}/attachments, then reload this page."),
                    "Device" => (s.DeviceId, "The demo account has registered no device. Register one with POST /api/Device, then reload this page."),
                    // Deleting goes to the least connected account, not to a friend.
                    "User" => (call.Method == OperationType.Delete ? s.StrangerUserId ?? s.OtherUserId : s.OtherUserId,
                               "There is no other account in the database."),
                    _ => (null, "No example row is known for this id.")
                };

            case "taskid":
                // Removing a link needs the task that really has one.
                return call.Path.Contains("{dependsOnTaskId}")
                    ? (s.WaitingTaskId, "None of the demo account's tasks waits for another. Link two with POST /api/Task/{taskId}/dependencies, then reload this page.")
                    : (s.TaskId, "The demo account has no task. Create one with POST /api/Task, then reload this page.");
            case "itemid":
                return (s.TaskId, "The demo account has no task. Create one with POST /api/Task, then reload this page.");
            case "dependsontaskid":
                return (s.DependsOnTaskId, "None of the demo account's tasks waits for another. Link two with POST /api/Task/{taskId}/dependencies, then reload this page.");
            case "subtaskid":
                return (s.SubtaskId, "None of the demo account's tasks has a checklist. Add a line with POST /api/Task/{taskId}/subtasks, then reload this page.");
            case "groupid":
                return (s.GroupId, "The demo account owns no project. Create one with POST /api/Group, then reload this page.");
            case "commentid":
                // A project's discussion has comments of its own.
                if (call.Area == "Group")
                    return (s.GroupCommentId, "The demo account has written nothing in a project's discussion yet. Add a comment with POST /api/Group/{groupId}/comments, then reload this page.");

                return (s.CommentId, "The demo account has written no comment yet. Add one with POST /api/Task/{taskId}/comments, then reload this page.");
            case "mentionid":
                return (s.MentionId, "Nobody has mentioned the demo account.");
            case "notificationid":
                return (s.NotificationId, "The demo account has no notifications right now.");

            case "friendshipid":
                return call.Path.Contains("/accept/") || call.Path.Contains("/reject/")
                    ? (s.PendingFriendshipId, "Nobody is waiting for an answer from the demo account. Accept and reject need a friend request that was sent TO this account.")
                    : (s.FriendshipId, "The demo account has no friendships. Send a request with POST /api/Friends/send-request/{receiverId}, then reload this page.");

            case "receiverid":
            case "usertoblockid":
                return (s.StrangerUserId, "Every other account already has a friendship with the demo account.");
            case "blockeduserid":
                return (s.BlockedUserId, "The demo account has blocked nobody. Block someone with PUT /api/Friends/block/{userToBlockId}, then reload this page.");

            case "conversationid":
                // Name, members and leaving only exist for a group chat.
                return IsGroupChatPath(call.Path)
                    ? (s.GroupConversationId, "The demo account is in no group chat. Create one with POST /api/Chat/groups, then reload this page.")
                    : (s.ConversationId, "The demo account has no chat yet. Open one with POST /api/Chat/with/{userId}, then reload this page.");
            case "messageid":
                return (s.ChatMessageId, "The demo account has sent no chat message. Send one with POST /api/Chat/{conversationId}/messages, then reload this page.");

            case "userid":
                if (call.Area == "Chat" && IsGroupChatPath(call.Path))
                    return (s.GroupChatMemberUserId, "The demo account is in no group chat with other people. Create one with POST /api/Chat/groups, then reload this page.");

                if (call.Area == "Chat")
                    return (s.ContactUserId ?? s.OtherUserId, "The demo account has no friends to chat with.");

                if (call.Area == "Event")
                    return (s.AttendeeUserId, "Nobody is invited to this event. Invite someone with POST /api/Event/{id}/attendees, then reload this page.");

                if (call.Area == "Group")
                {
                    return call.Method == OperationType.Post
                        ? (s.NonMemberUserId, "Everyone is already a member of this project.")
                        : (s.GroupMemberUserId, "This project has no members besides its owner. Add one with POST /api/Group/{groupId}/members/{userId}, then reload this page.");
                }
                return (s.OtherUserId, "There is no other account in the database.");

            default:
                return (null, $"No example row is known for '{name}'.");
        }
    }

    private static bool IsGroupChatPath(string path) =>
        path.EndsWith("/name") || path.EndsWith("/leave") || path.Contains("/members");

    /// <summary>
    /// Which event an address is about depends on what is done to it: single
    /// dates and removing a guest need the repeating event that has guests,
    /// answering needs an invitation, everything else takes a plain event.
    /// </summary>
    private static (Guid? Id, string Missing) EventId(Call call)
    {
        var s = call.Samples;

        if (call.Path.EndsWith("/respond"))
            return (s.InvitedEventId, "No invitation is waiting for an answer from the demo account.");

        if (call.Path.Contains("/occurrences") || call.Path.Contains("/attendees/"))
            return (s.RepeatingEventId, "The demo account has no repeating event. Create one with POST /api/Event (recurrence: Weekly), then reload this page.");

        return (s.EventId, "The demo account has no event. Create one with POST /api/Event, then reload this page.");
    }

    private static void ApplyQuery(OpenApiParameter parameter, Call call)
    {
        switch (parameter.Name.ToLowerInvariant())
        {
            // A range the calendar would ask for: yesterday to 30 days ahead.
            case "from":
                parameter.Example = Date(DateTime.UtcNow.Date.AddDays(-1));
                break;
            case "to":
                parameter.Example = Date(DateTime.UtcNow.Date.AddDays(30));
                break;
            case "occurrencestart":
                parameter.Example = Date(call.Samples.OccurrenceStart ?? DateTime.UtcNow.Date.AddDays(7));
                break;

            case "query":
                parameter.Example = new OpenApiString(call.Samples.SearchWord);
                break;

            case "pageindex":
                parameter.Example = new OpenApiInteger(1);
                break;
            case "pagesize":
                parameter.Example = new OpenApiInteger(10);
                break;
            case "withindays":
                parameter.Example = new OpenApiInteger(7);
                break;

            // Optional filters are left empty so the first try returns
            // everything; the allowed values are offered as a drop-down.
            case "status":
                Choices(parameter, Enum.GetNames<Miqat.Domain.Enumerations.TaskStatus>());
                break;
            case "priority":
                Choices(parameter, Enum.GetNames<Miqat.Domain.Enumerations.Priority>());
                break;
            case "kind":
                Choices(parameter, Enum.GetNames<Miqat.Domain.Enumerations.BoardKind>());
                break;

            case "groupid":
                Describe(parameter, call.Samples.GroupId.HasValue
                    ? $"Only tasks of this project, for example {call.Samples.GroupId}."
                    : "Only tasks of this project.");
                break;
            case "duefrom":
                Describe(parameter, $"Due on or after this day, for example {DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}.");
                break;
            case "dueto":
                Describe(parameter, $"Due on or before this day, for example {DateTime.UtcNow.AddDays(30):yyyy-MM-dd}.");
                break;
        }
    }

    private static void Choices(OpenApiParameter parameter, IEnumerable<string> values)
    {
        if (parameter.Schema == null) return;
        parameter.Schema.Enum = values.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
    }

    private static void Describe(OpenApiParameter parameter, string text)
    {
        if (string.IsNullOrWhiteSpace(parameter.Description)) parameter.Description = text;
    }

    // ── Request bodies ──────────────────────────────────────────────────────

    private static void ApplyBody(OpenApiDocument document, OpenApiOperation operation, Call call)
    {
        if (operation.RequestBody == null) return;

        foreach (var (mediaType, media) in operation.RequestBody.Content)
        {
            if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) continue;

            var schema = Resolve(document, media.Schema, out var schemaName);
            if (schema == null || schema.Properties.Count == 0) continue;

            // One endpoint, two uses: offer both in the examples drop-down.
            if (call.Path.EndsWith("/verify-otp", StringComparison.OrdinalIgnoreCase))
            {
                media.Examples = new Dictionary<string, OpenApiExample>
                {
                    ["New account"] = new()
                    {
                        Summary = "Confirm the email of the account you just registered",
                        Value = Build(document, schema, schemaName, call, call.NewAccountScenario())
                    },
                    ["Password reset"] = new()
                    {
                        Summary = "Check the code sent by forgot-password",
                        Value = Build(document, schema, schemaName, call, call.DemoAccountScenario("PasswordReset"))
                    }
                };
                continue;
            }

            media.Example = Build(document, schema, schemaName, call, call.DefaultScenario());
        }
    }

    private static OpenApiSchema? Resolve(OpenApiDocument document, OpenApiSchema? schema, out string name)
    {
        name = string.Empty;
        if (schema == null) return null;
        if (schema.Reference == null) return schema;

        name = schema.Reference.Id;
        return document.Components?.Schemas != null &&
               document.Components.Schemas.TryGetValue(name, out var resolved)
            ? resolved
            : null;
    }

    private static OpenApiObject Build(
        OpenApiDocument document, OpenApiSchema schema, string schemaName, Call call, Scenario scenario)
    {
        var body = new OpenApiObject();

        foreach (var (name, property) in schema.Properties)
        {
            if (IsFilledInByTheServer(schemaName, name)) continue;

            var value = ValueFor(document, schemaName, name, property, call, scenario);
            if (value != null) body[name] = value;
        }

        return body;
    }

    /// <summary>
    /// The list and detail DTOs double as request bodies, so they carry fields
    /// the server works out itself. Leaving them out keeps the example to what
    /// a client actually sends.
    /// </summary>
    private static bool IsFilledInByTheServer(string schemaName, string name)
    {
        var key = name.ToLowerInvariant();

        if (key is "id" or "createdat" or "updatedat" or "completedat" or "isoverdue" or "isowner"
            or "isverified" or "ownerid" or "ownername" or "groupname" or "groupcolor" or "assignedtousername")
            return true;

        if (key.EndsWith("count") || key.EndsWith("profilepictureurl")) return true;

        return schemaName switch
        {
            // The owner of a task is the caller.
            "TaskDto" => key is "userid" or "isblocked",
            // The time zone belongs to the profile, not to the calendar preferences.
            "CalendarSettingsDto" => key == "timezone",
            // Role and active state have their own endpoint: PUT /api/User/{id}/access.
            "UserDto" => key is "role" or "isactive",
            _ => false
        };
    }

    private static IOpenApiAny? ValueFor(
        OpenApiDocument document, string schemaName, string name, OpenApiSchema property, Call call, Scenario scenario)
    {
        var s = call.Samples;
        var fake = call.Fake;
        var person = scenario.Person;
        var isBoard = schemaName.Contains("Board", StringComparison.OrdinalIgnoreCase);
        var isProject = schemaName.StartsWith("Group", StringComparison.OrdinalIgnoreCase);
        var isEvent = schemaName.Contains("Event", StringComparison.OrdinalIgnoreCase);
        var isCalendar = schemaName.Contains("Calendar", StringComparison.OrdinalIgnoreCase);
        var isSubtask = schemaName.Contains("Subtask", StringComparison.OrdinalIgnoreCase);

        // One date of a repeating event, moved an hour later; otherwise tomorrow at ten.
        var isOccurrence = schemaName.Contains("Occurrence", StringComparison.OrdinalIgnoreCase);
        var occurrenceStart = s.OccurrenceStart ?? DateTime.UtcNow.Date.AddDays(7).AddHours(10);
        var eventStart = isOccurrence ? occurrenceStart.AddHours(1) : DateTime.UtcNow.Date.AddDays(1).AddHours(10);

        switch (name.ToLowerInvariant())
        {
            // ── Accounts ────────────────────────────────────────────────────
            case "email": return Text(person.Email);
            case "password":
            case "currentpassword":
            case "newpassword":
            case "confirmpassword":
                return Text(scenario.Password);
            case "code": return Text(OtpPlaceholder);
            case "token":
                return Text(schemaName.Contains("Google", StringComparison.OrdinalIgnoreCase)
                    ? "paste-a-Google-ID-token-here"
                    : OtpPlaceholder);
            case "purpose": return Text(scenario.Purpose);
            case "refreshtoken": return Text(RefreshTokenPlaceholder);

            // ── People ──────────────────────────────────────────────────────
            case "fullname": return Text(person.FullName);
            case "phonenumber": return Text(person.PhoneNumber ?? fake.Person.PhoneNumber!);
            case "country": return Text(person.Country ?? fake.Person.Country!);
            case "timezone": return Text(person.TimeZone);
            case "gender": return Text(person.Gender ?? fake.Person.Gender!);
            case "dateofbirth": return Date(person.DateOfBirth ?? fake.Person.DateOfBirth!.Value);
            case "bio": return Text(person.Bio ?? fake.Bio);
            case "role": return Text("User");

            // ── Tasks, projects, boards, comments ───────────────────────────
            case "title": return Text(isEvent || isOccurrence ? fake.EventTitle : isSubtask ? fake.ChecklistLine : fake.TaskTitle);
            case "description":
                return Text(isProject ? fake.ProjectDescription : isEvent ? fake.EventDescription : fake.TaskDescription);
            case "name":
                if (schemaName.Contains("Device", StringComparison.OrdinalIgnoreCase)) return Text("Chrome on Windows");
                if (schemaName is "CreateGroupDto" or "RenameGroupDto") return Text(fake.ChatGroupName);
                return Text(isBoard ? fake.BoardName : isCalendar ? fake.CalendarName : fake.ProjectName);
            case "color": return Text(fake.Color);
            case "tags": return Text(fake.TaskTags);
            // "Pending" is how a task starts; an invitation is answered with a yes.
            case "status": return Text(schemaName.StartsWith("Respond", StringComparison.OrdinalIgnoreCase) ? "Accepted" : "Pending");
            case "priority": return Text("High");
            case "recurrence": return Text("None");
            case "startdate": return Date(DateTime.UtcNow.Date.AddDays(1).AddHours(9));
            case "duedate": return Date(DateTime.UtcNow.Date.AddDays(7).AddHours(17));
            case "recurrenceenddate": return null;
            case "content": return Text(isBoard ? fake.BoardContent : fake.Comment);
            case "kind": return Text("Whiteboard");
            case "text": return Text($"Hi @{FirstName(s.OtherUser?.FullName ?? fake.Person.FullName)}, can you take a look?");

            // ── Calendar ────────────────────────────────────────────────────
            case "startsat": return Date(eventStart);
            case "endsat": return Date(eventStart.AddHours(1));
            case "occurrencestart": return Date(occurrenceStart);
            case "isallday":
            case "cancelled":
            case "isdone":
            case "use24hourclock":
                return new OpenApiBoolean(false);
            case "location": return Text(fake.EventLocation);
            case "recurrenceinterval": return new OpenApiInteger(1);
            case "reminderminutes": return new OpenApiArray { new OpenApiInteger(15) };
            case "minutesbefore": return new OpenApiInteger(30);
            case "minutes": return new OpenApiInteger(10);
            case "durationminutes":
            case "defaulteventminutes":
                return new OpenApiInteger(60);
            case "defaultreminderminutes": return new OpenApiInteger(15);
            case "maxresults": return new OpenApiInteger(5);
            case "position": return new OpenApiInteger(0);
            case "workdaystart": return Text("09:00");
            case "workdayend": return Text("17:00");
            case "workdays":
                var workDays = new OpenApiArray();
                foreach (var day in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }) workDays.Add(Text(day));
                return workDays;

            // Left out on purpose: the default calendar, "from now", "two weeks".
            case "calendarid":
            case "from":
            case "to":
                return null;

            // People to invite or to find a time with: someone the account is connected to.
            case "attendeeids":
            case "userids":
                var people = new OpenApiArray();
                // Adding to a group chat: a friend who is not in it yet.
                var personId = schemaName == "AddMembersDto" ? s.GroupChatCandidateUserId ?? s.ContactUserId : s.ContactUserId;
                if (personId.HasValue) people.Add(Text(personId.Value.ToString()));
                return people;

            // A new group chat starts with two friends.
            case "memberids":
                var members = new OpenApiArray();
                foreach (var friendId in s.FriendIds) members.Add(Text(friendId.ToString()));
                return members;

            case "dependsontaskid": return IdOrNothing(s.UnlinkedTaskId, s);
            case "newemail": return Text(fake.NewEmail);

            // A chat message, and the made-up id an app gives it before it is saved.
            case "body": return Text(fake.ChatLine);
            case "clientid": return Text($"local-{Guid.NewGuid():N}"[..20]);

            // A push subscription in the shape a browser hands out.
            case "endpoint": return Text("https://fcm.googleapis.com/fcm/send/example-subscription");
            case "p256dh": return Text("BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM");
            case "auth": return Text("tBHItJI5svbpez7KI4CCXg");

            // ── Links to other rows ─────────────────────────────────────────
            case "assignedtouserid": return IdOrNothing(s.GroupMemberUserId ?? s.OtherUserId, s);
            case "groupid": return IdOrNothing(s.GroupId, s);
            case "entityid": return IdOrNothing(s.TaskId, s);
            case "entitytype": return Text("Task");
            case "mentioneduserids":
                var mentioned = new OpenApiArray();
                // In a project's discussion only people on the project can be mentioned.
                var mentionedId = schemaName == "CreateGroupCommentDto" ? s.GroupMemberUserId ?? s.OtherUserId : s.OtherUserId;
                if (mentionedId.HasValue) mentioned.Add(Text(mentionedId.Value.ToString()));
                return mentioned;

            // A comment of its own; give the id of another comment here to answer it.
            case "parentid": return null;
        }

        return Fallback(document, name, property);
    }

    /// <summary>A sensible value for a field this class has never heard of.</summary>
    private static IOpenApiAny? Fallback(OpenApiDocument document, string name, OpenApiSchema property)
    {
        var schema = Resolve(document, property, out _) ?? property;

        if (schema.Enum is { Count: > 0 }) return schema.Enum[0];

        return schema.Type switch
        {
            "boolean" => new OpenApiBoolean(true),
            "integer" => new OpenApiInteger(1),
            "number" => new OpenApiDouble(1),
            "array" => new OpenApiArray(),
            "string" => schema.Format switch
            {
                "uuid" => Text(MadeUpId.ToString()),
                "date-time" => Date(DateTime.UtcNow.Date.AddDays(7)),
                "date" => Text(DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                _ => Text($"Sample {Words(name)}")
            },
            _ => null
        };
    }

    // ── Notes shown under an endpoint ───────────────────────────────────────

    private static void ApplyNotes(OpenApiOperation operation, Call call)
    {
        var notes = new List<string>();
        var path = call.Path.ToLowerInvariant();

        if (path.EndsWith("/auth/login"))
            notes.Add(call.Samples.IsLive
                ? "Start here. The demo account is already filled in; after it answers, this page signs you in and adds the token to every other request."
                : "After it answers, this page signs you in and adds the token to every other request.");
        else if (path.EndsWith("/auth/verify-otp") || path.EndsWith("/auth/reset-password"))
            notes.Add($"Replace {OtpPlaceholder} with the 6-digit code from the email. In development no email is sent: the code is printed in the API log on a line that starts with [Dev OTP].");
        else if (path.EndsWith("/auth/google"))
            notes.Add("Needs a real ID token issued by Google for this app; it cannot be made up.");
        else if (path.EndsWith("/auth/refresh") || path.EndsWith("/auth/logout"))
            notes.Add("The refresh token is filled in automatically from your last sign-in on this page.");
        else if (path.EndsWith("/upload-profile-image"))
            notes.Add("Choose a JPG, PNG, GIF or WebP file.");
        else if (path.EndsWith("/calendar/import"))
            notes.Add("Choose an .ics file, for example one exported from Google Calendar or from GET /api/Calendar/export.");
        else if (path.EndsWith("/attachments") && call.Method == OperationType.Post)
            notes.Add("Choose a file: a document, an image or a zip, 10 MB at most.");
        else if (path.EndsWith("/auth/change-email"))
            notes.Add("Sends a code to the new address; nothing changes yet. In development no email is sent: the code is printed in the API log on a line that starts with [Dev OTP].");
        else if (path.EndsWith("/auth/change-email/confirm"))
            notes.Add($"Replace {OtpPlaceholder} with the code that was sent to the new address. Confirming changes the address the demo account signs in with, so the prefilled login stops working afterwards.");
        else if (path.EndsWith("/device") && call.Method == OperationType.Post)
            notes.Add("A browser fills these three values in from its own push subscription; the ones in the example only show their shape.");

        if (path.EndsWith("/auth/change-password") || path.EndsWith("/auth/reset-password"))
            notes.Add("The example sets the same password again, so the sign-in example keeps working.");

        if (call.Method == OperationType.Delete && call.Area == "User" && path.EndsWith("{id}"))
            notes.Add("This deletes the account in the example for good (admin only).");

        notes.AddRange(call.Notes.Distinct());
        if (notes.Count == 0) return;

        var text = string.Join("\n\n", notes);
        operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? text
            : operation.Description + "\n\n" + text;
    }

    private static string Intro(SwaggerSamples samples)
    {
        var lines = new List<string>
        {
            "**Every endpoint is prefilled with example data, so you can try it without typing.**",
            "",
            "1. Open **Auth** and run **POST /api/Auth/login**." +
                (samples.IsLive ? " The demo account is already filled in." : ""),
            "2. This page then signs you in: the token is added to every other request. " +
                "It lasts 15 minutes; run login again when you start getting 401.",
            "3. Open any endpoint and press **Execute**.",
            ""
        };

        lines.Add(samples.IsLive
            ? "The ids in the examples are real rows that belong to the demo account. " +
              "Reload the page after you create or delete something to get fresh ones."
            : "Demo data is switched off here (Seed:DemoData), so the ids in the examples are made up: " +
              "replace them with real ones.");

        return string.Join("\n", lines);
    }

    // ── Small helpers ───────────────────────────────────────────────────────

    private static OpenApiString Text(string value) => new(value);

    private static OpenApiString Date(DateTime value) =>
        new(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    /// <summary>A real id, or a made-up one when demo data is off, or nothing at all.</summary>
    private static IOpenApiAny? IdOrNothing(Guid? id, SwaggerSamples samples) =>
        id.HasValue ? Text(id.Value.ToString()) : samples.IsLive ? null : Text(MadeUpId.ToString());

    private static string FirstName(string fullName) =>
        new string(fullName.Trim().TakeWhile(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() is { Length: > 0 } first
            ? first
            : "sara";

    /// <summary>"mentionedUserIds" -> "mentioned user ids".</summary>
    private static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    /// <summary>Whose details go into a request body.</summary>
    private sealed record Scenario(SamplePerson Person, string Password, string Purpose);

    /// <summary>One endpoint while its examples are being written.</summary>
    private sealed class Call
    {
        public Call(string path, OperationType method, SwaggerSamples samples, FakeData fake)
        {
            Path = path;
            Method = method;
            Samples = samples;
            Fake = fake;

            // "/api/Task/{id}" -> "Task"
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Area = segments.Length > 1 ? segments[1] : string.Empty;
        }

        public string Path { get; }
        public OperationType Method { get; }
        public SwaggerSamples Samples { get; }
        public FakeData Fake { get; }
        public string Area { get; }
        public List<string> Notes { get; } = new();

        private SamplePerson DemoAccount =>
            Samples.Me ?? new SamplePerson("Demo User", Samples.Email, null, null, "UTC", null, null, null);

        public Scenario NewAccountScenario() => new(Fake.Person, Fake.Password, "EmailVerification");

        public Scenario DemoAccountScenario(string purpose) => new(DemoAccount, Samples.Password, purpose);

        public Scenario DefaultScenario()
        {
            var path = Path.ToLowerInvariant();

            // A brand-new person.
            if (path.EndsWith("/auth/register") || path.EndsWith("/auth/resend-otp"))
                return NewAccountScenario();

            // The demo account itself.
            if (path.EndsWith("/auth/login") || path.EndsWith("/auth/forgot-password") ||
                path.EndsWith("/auth/reset-password") || path.EndsWith("/auth/change-password") ||
                path.EndsWith("/auth/change-email"))
                return DemoAccountScenario("PasswordReset");

            // Profile forms start from what is stored, so running the example
            // does not wipe the account's details.
            if (path.EndsWith("/user/me"))
                return DemoAccountScenario("EmailVerification");

            if (Area == "User" && Samples.OtherUser != null)
                return new Scenario(Samples.OtherUser, Samples.Password, "EmailVerification");

            return NewAccountScenario();
        }
    }
}
