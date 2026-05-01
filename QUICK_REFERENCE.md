# Quick Reference Guide: Friends & Mention System

## Quick Start for Developers

### Testing Friend Request Flow
```bash
# 1. User A sends friend request to User B
POST /api/friends/send-request/{userBId}
Authorization: Bearer {userAToken}

# 2. User B accepts the request
PUT /api/friends/accept/{friendshipId}
Authorization: Bearer {userBToken}

# 3. Both users can now see each other in friends list
GET /api/friends
Authorization: Bearer {userAToken}
```

### Testing Mention Flow
```bash
# 1. Parse mentions from text
POST /api/mentions/parse
Authorization: Bearer {token}
Body: { "text": "Hey @john and @jane, check this!" }
Response: { "mentionedUserIds": ["guid1", "guid2"] }

# 2. Create mentions for users
POST /api/mentions/create
Authorization: Bearer {token}
Body: { 
  "mentionedUserIds": ["guid1", "guid2"],
  "entityType": "Task",
  "entityId": "taskGuid"
}

# 3. Mentioned user gets notification and can view mentions
GET /api/mentions/unread
Authorization: Bearer {mentionedUserToken}
```

---

## Key Classes to Know

### Services
- **FriendService** - Located at `Miqat.Application.Services.FriendService`
  - Dependency: `IUnitOfWork`, `INotificationService`
  - Register in DI: Already registered in Program.cs

- **MentionService** - Located at `Miqat.Application.Services.MentionService`
  - Dependency: `IUnitOfWork`, `INotificationService`
  - Register in DI: Already registered in Program.cs

### Entities
- **Friendship** - Located at `Miqat.Domain.Entities.Friendship`
- **Mention** - Located at `Miqat.Domain.Entities.Mention`
- **User** - Extended with navigation properties (inherited from existing)

### DTOs
- **FriendshipDto** - Located at `Miqat.Application.Modules.FriendshipDto`
- **MentionDto** - Located at `Miqat.Application.Modules.MentionDto`

### Controllers
- **FriendsController** - Located at `Miqat.Persistence.Controllers.FriendsController`
- **MentionsController** - Located at `Miqat.Persistence.Controllers.MentionsController`

---

## Common Workflows

### Adding Mention Support to Existing Entity
If you want to add mention detection to other entities (e.g., Comments), follow this pattern:

```csharp
// In your service/controller where the entity is created or updated:
var mentionService = serviceProvider.GetRequiredService<IMentionService>();

// Parse mentions from the text
var mentionedUserIds = await mentionService.ParseMentionsFromTextAsync(entityText);

// Create mentions if any were found
if (mentionedUserIds.Any())
{
	await mentionService.CreateMentionsAsync(
		currentUserId,
		mentionedUserIds,
		EntityType.Comment, // or appropriate type
		entityId
	);
}
```

### Retrieving User's Friend List in Frontend
```
GET /api/friends
Authorization: Bearer {token}

Returns list of FriendshipDto objects where Status = "Accepted"
```

### Getting Friend Request Status
```
Check pending requests: GET /api/friends/pending
Check sent requests: GET /api/friends/sent
```

---

## Database Queries (EF Core)

### Find all pending friend requests for a user
```csharp
var pending = await _unitOfWork.Repository<Friendship>()
	.FindAsync(f => f.ReceiverId == userId && 
					f.Status == FriendshipStatus.Pending &&
					!f.IsDeleted);
```

### Find all unread mentions for a user
```csharp
var unread = await _unitOfWork.Repository<Mention>()
	.FindAsync(m => m.MentionedUserId == userId && 
					!m.IsRead && 
					!m.IsDeleted);
```

### Check if two users are friends
```csharp
var areFriends = await _unitOfWork.Repository<Friendship>()
	.FindAsync(f => f.Status == FriendshipStatus.Accepted &&
					(f.SenderId == userId1 && f.ReceiverId == userId2 ||
					 f.SenderId == userId2 && f.ReceiverId == userId1) &&
					!f.IsDeleted);
```

---

## Error Codes & Messages

### Friend System
| Status | Scenario |
|--------|----------|
| 200 | Request successful |
| 400 | Cannot friend self, duplicate request, or unauthorized action |
| 404 | Friendship not found |
| 500 | Server error |

### Mention System
| Status | Scenario |
|--------|----------|
| 200 | Request successful |
| 400 | Invalid input or duplicate mention |
| 404 | Mention not found or not authorized |
| 500 | Server error |

---

## Important Validations

### Friendship Validations
```csharp
if (senderId == receiverId)
	throw new InvalidOperationException("Cannot send friend request to yourself");

if (existingFriendship.Any())
	throw new InvalidOperationException("Friendship already exists");

if (friendship.ReceiverId != receiverId)
	throw new InvalidOperationException("Only receiver can accept");
```

### Mention Validations
```csharp
if (mentionedByUserId == mentionedUserId)
	throw new ArgumentException("Cannot mention yourself");

// Duplicate check prevents multiple mentions for same user in same entity
if (existingMention.Any())
	continue; // Skip this mention
```

---

## Notification Integration

When friend requests or mentions are created, notifications are automatically generated:

### Friend Request Notification
```csharp
new Notification(
	title: "New Friend Request",
	message: $"{sender.FullName} sent you a friend request.",
	type: NotificationType.FriendRequestSent, // or FriendRequestAccepted
	recipientUserId: receiverId,
	triggeredByUserId: senderId,
	linkedEntityId: friendship.Id,
	linkedEntityType: "Friendship"
);
```

### Mention Notification
```csharp
new Notification(
	title: "You were mentioned",
	message: $"{mentioningUser.FullName} mentioned you in a {entityType}.",
	type: NotificationType.MentionedInTask,
	recipientUserId: mentionedUserId,
	triggeredByUserId: mentioningUserId,
	linkedEntityId: entityId,
	linkedEntityType: entityType.ToString()
);
```

---

## Pattern: Mention Parsing Logic

The mention service uses this regex pattern to detect mentions:
```
@(\w+)  - Matches @word_characters
```

The service then looks up users by:
1. Checking if username matches Email (case-insensitive)
2. Checking if username matches FullName (case-insensitive partial match)

Example:
- Text: "Hey @john and @Jane Smith!"
- Patterns found: "john", "Jane"
- Lookup: Searches for Email containing "john" or FullName containing "john"
- Results: User objects if found, skipped if not

---

## Deployment Checklist

- [x] Code compiled successfully
- [x] Migration file generated
- [x] Services registered in DI container
- [ ] Database migration applied (automatic on startup)
- [ ] Test friend request creation
- [ ] Test mention detection
- [ ] Test notifications received
- [ ] Frontend updated with new endpoints
- [ ] API documentation reviewed
- [ ] Error handling tested

---

## Performance Tips

1. **Use include/ThenInclude when fetching with navigation**
   ```csharp
   // Instead of lazy loading, pre-load related data
   var friendships = await _unitOfWork.Repository<Friendship>()
	   .FindAsync(f => f.SenderId == userId);
   // Then manually fetch User data as needed
   ```

2. **Leverage indexes on database**
   - (SenderId, ReceiverId) unique index for friendships
   - (MentionedUserId, IsRead) composite index for mentions

3. **Consider pagination for large lists**
   ```csharp
   // Add skip/take for friend lists and mentions
   var friends = await GetFriendsAsync(userId);
   var paginated = friends.Skip((page-1)*pageSize).Take(pageSize);
   ```

---

## Troubleshooting

### Migration Failed
- Check database connection string in appsettings.json
- Ensure PostgreSQL is running
- Clear any pending migrations: `dotnet ef migrations remove`

### Service Not Registered
- Verify Program.cs has: `builder.Services.AddScoped<IFriendService, FriendService>();`
- Check namespace imports are correct

### Mentions Not Detected
- Verify regex pattern matches: `@` followed by word characters
- Check username/email in database matches query (case sensitivity)
- Ensure text parsing is called before mention creation

### Notifications Not Created
- Verify INotificationService is injected
- Check NotificationService.CreateAsync is called
- Ensure user IDs are valid (not Guid.Empty)

---

## Future Work

See FRIENDS_MENTION_SYSTEM_API_DOCUMENTATION.md for enhancement ideas and recommendations.
