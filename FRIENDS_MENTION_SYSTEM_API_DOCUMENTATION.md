# Friends System & Mention System API Documentation

## Overview
This document describes the new Friends System and Mention System features added to the Miqat API.

## Friends System

### Endpoints

#### 1. Send Friend Request
- **Endpoint**: `POST /api/friends/send-request/{receiverId}`
- **Authorization**: Required (Bearer Token)
- **Parameters**: 
  - `receiverId` (path parameter, Guid): The ID of the user to send the friend request to
- **Response**: 
  ```json
  {
	"message": "Friend request sent successfully.",
	"friendship": {
	  "id": "guid",
	  "senderId": "guid",
	  "senderName": "string",
	  "senderProfilePictureUrl": "string or null",
	  "receiverId": "guid",
	  "receiverName": "string",
	  "receiverProfilePictureUrl": "string or null",
	  "status": "Pending",
	  "createdAt": "2025-05-01T12:00:00Z",
	  "updatedAt": null
	}
  }
  ```
- **Error Cases**:
  - 400: User cannot send a friend request to themselves
  - 400: Friendship request already exists between these users
  - 500: Unexpected error

#### 2. Accept Friend Request
- **Endpoint**: `PUT /api/friends/accept/{friendshipId}`
- **Authorization**: Required (Bearer Token)
- **Parameters**:
  - `friendshipId` (path parameter, Guid): The ID of the friendship request
- **Response**: 
  ```json
  {
	"message": "Friend request accepted successfully."
  }
  ```
- **Error Cases**:
  - 400: Only the receiver can accept this friend request
  - 404: Friendship request not found
  - 500: Unexpected error

#### 3. Reject Friend Request
- **Endpoint**: `PUT /api/friends/reject/{friendshipId}`
- **Authorization**: Required (Bearer Token)
- **Parameters**:
  - `friendshipId` (path parameter, Guid): The ID of the friendship request
- **Response**: 
  ```json
  {
	"message": "Friend request rejected successfully."
  }
  ```
- **Error Cases**:
  - 404: Friendship request not found
  - 500: Unexpected error

#### 4. Block User
- **Endpoint**: `PUT /api/friends/block/{userToBlockId}`
- **Authorization**: Required (Bearer Token)
- **Parameters**:
  - `userToBlockId` (path parameter, Guid): The ID of the user to block
- **Response**: 
  ```json
  {
	"message": "User blocked successfully."
  }
  ```
- **Error Cases**:
  - 400: User cannot block themselves
  - 400: User with ID not found
  - 500: Unexpected error

#### 5. Unblock User
- **Endpoint**: `PUT /api/friends/unblock/{blockedUserId}`
- **Authorization**: Required (Bearer Token)
- **Parameters**:
  - `blockedUserId` (path parameter, Guid): The ID of the user to unblock
- **Response**: 
  ```json
  {
	"message": "User unblocked successfully."
  }
  ```
- **Error Cases**:
  - 404: No blocked user found
  - 500: Unexpected error

#### 6. Get All Friends
- **Endpoint**: `GET /api/friends`
- **Authorization**: Required (Bearer Token)
- **Response**: 
  ```json
  {
	"friends": [
	  {
		"id": "guid",
		"senderId": "guid",
		"senderName": "string",
		"senderProfilePictureUrl": "string or null",
		"receiverId": "guid",
		"receiverName": "string",
		"receiverProfilePictureUrl": "string or null",
		"status": "Accepted",
		"createdAt": "2025-05-01T12:00:00Z",
		"updatedAt": "2025-05-01T13:00:00Z"
	  }
	]
  }
  ```
- **Error Cases**:
  - 500: Unexpected error

#### 7. Get Pending Requests (Received)
- **Endpoint**: `GET /api/friends/pending`
- **Authorization**: Required (Bearer Token)
- **Response**: 
  ```json
  {
	"pendingRequests": [
	  {
		"id": "guid",
		"senderId": "guid",
		"senderName": "string",
		"senderProfilePictureUrl": "string or null",
		"receiverId": "guid",
		"receiverName": "string",
		"receiverProfilePictureUrl": "string or null",
		"status": "Pending",
		"createdAt": "2025-05-01T12:00:00Z",
		"updatedAt": null
	  }
	]
  }
  ```
- **Error Cases**:
  - 500: Unexpected error

#### 8. Get Sent Requests
- **Endpoint**: `GET /api/friends/sent`
- **Authorization**: Required (Bearer Token)
- **Response**: 
  ```json
  {
	"sentRequests": [
	  {
		"id": "guid",
		"senderId": "guid",
		"senderName": "string",
		"senderProfilePictureUrl": "string or null",
		"receiverId": "guid",
		"receiverName": "string",
		"receiverProfilePictureUrl": "string or null",
		"status": "Pending",
		"createdAt": "2025-05-01T12:00:00Z",
		"updatedAt": null
	  }
	]
  }
  ```
- **Error Cases**:
  - 500: Unexpected error

#### 9. Get Friendship Details
- **Endpoint**: `GET /api/friends/{friendshipId}`
- **Authorization**: Required (Bearer Token)
- **Parameters**:
  - `friendshipId` (path parameter, Guid): The ID of the friendship
- **Response**: 
  ```json
  {
	"id": "guid",
	"senderId": "guid",
	"senderName": "string",
	"senderProfilePictureUrl": "string or null",
	"receiverId": "guid",
	"receiverName": "string",
	"receiverProfilePictureUrl": "string or null",
	"status": "Accepted",
	"createdAt": "2025-05-01T12:00:00Z",
	"updatedAt": "2025-05-01T13:00:00Z"
  }
  ```
- **Error Cases**:
  - 404: Friendship not found
  - 500: Unexpected error

---

## Mention System

### Endpoints

#### 1. Get All Mentions
- **Endpoint**: `GET /api/mentions`
- **Authorization**: Required (Bearer Token)
- **Response**: 
  ```json
  {
	"mentions": [
	  {
		"id": "guid",
		"mentionedByUserId": "guid",
		"mentionedByUserName": "string",
		"mentionedByUserProfilePictureUrl": "string or null",
		"mentionedUserId": "guid",
		"entityType": "Task",
		"entityId": "guid",
		"isRead": false,
		"createdAt": "2025-05-01T12:00:00Z"
	  }
	]
  }
  ```
- **Error Cases**:
  - 500: Unexpected error

#### 2. Get Unread Mentions
- **Endpoint**: `GET /api/mentions/unread`
- **Authorization**: Required (Bearer Token)
- **Response**: 
  ```json
  {
	"unreadMentions": [
	  {
		"id": "guid",
		"mentionedByUserId": "guid",
		"mentionedByUserName": "string",
		"mentionedByUserProfilePictureUrl": "string or null",
		"mentionedUserId": "guid",
		"entityType": "Task",
		"entityId": "guid",
		"isRead": false,
		"createdAt": "2025-05-01T12:00:00Z"
	  }
	]
  }
  ```
- **Error Cases**:
  - 500: Unexpected error

#### 3. Get Unread Mentions Count
- **Endpoint**: `GET /api/mentions/unread-count`
- **Authorization**: Required (Bearer Token)
- **Response**: 
  ```json
  {
	"unreadCount": 5
  }
  ```
- **Error Cases**:
  - 500: Unexpected error

#### 4. Mark Mention as Read
- **Endpoint**: `PUT /api/mentions/{mentionId}/read`
- **Authorization**: Required (Bearer Token)
- **Parameters**:
  - `mentionId` (path parameter, Guid): The ID of the mention
- **Response**: 
  ```json
  {
	"message": "Mention marked as read."
  }
  ```
- **Error Cases**:
  - 404: Mention not found or not authorized
  - 500: Unexpected error

#### 5. Parse Mentions from Text
- **Endpoint**: `POST /api/mentions/parse`
- **Authorization**: Required (Bearer Token)
- **Request Body**: 
  ```json
  {
	"text": "Hey @john and @jane, check out this task!"
  }
  ```
- **Response**: 
  ```json
  {
	"mentionedUserIds": ["guid1", "guid2"]
  }
  ```
- **Error Cases**:
  - 400: Text is required
  - 500: Unexpected error
- **Notes**:
  - Parses @username patterns from text
  - Matches against user emails and full names (case-insensitive)
  - Returns unique user IDs of mentioned users

#### 6. Create Mentions
- **Endpoint**: `POST /api/mentions/create`
- **Authorization**: Required (Bearer Token)
- **Request Body**: 
  ```json
  {
	"mentionedUserIds": ["guid1", "guid2"],
	"entityType": "Task",
	"entityId": "guid"
  }
  ```
- **Response**: 
  ```json
  {
	"message": "Mentions created successfully.",
	"mentions": [
	  {
		"id": "guid",
		"mentionedByUserId": "guid",
		"mentionedByUserName": "string",
		"mentionedByUserProfilePictureUrl": "string or null",
		"mentionedUserId": "guid",
		"entityType": "Task",
		"entityId": "guid",
		"isRead": false,
		"createdAt": "2025-05-01T12:00:00Z"
	  }
	]
  }
  ```
- **Error Cases**:
  - 400: At least one mentioned user ID is required
  - 400: EntityId is required
  - 400: User not found
  - 500: Unexpected error
- **Notes**:
  - User cannot mention themselves
  - Duplicate mentions for the same user in same entity are skipped
  - Creates notification for each mentioned user

---

## Business Rules & Validations

### Friends System Rules:
1. ✅ A user cannot send a friend request to themselves
2. ✅ A user cannot send a duplicate friend request (checked bidirectionally)
3. ✅ Only the receiver can accept or reject a friend request
4. ✅ Friend requests have status: Pending, Accepted, Rejected, or Blocked
5. ✅ When a friend request is accepted, a notification is sent to the sender
6. ✅ Blocking a user creates a blocked friendship relationship

### Mention System Rules:
1. ✅ Mentions are detected via @username pattern matching
2. ✅ Matches against user emails and full names (case-insensitive)
3. ✅ A user cannot mention themselves
4. ✅ Duplicate mentions for the same user in the same entity are ignored
5. ✅ Mentions are created with IsRead = false by default
6. ✅ Only the mentioned user can mark a mention as read
7. ✅ Each mention creates a notification for the mentioned user
8. ✅ Entity types supported: Project, Task, Comment

---

## Database Schema

### Friendship Table
- `Id` (Guid, Primary Key)
- `SenderId` (Guid, Foreign Key to Users)
- `ReceiverId` (Guid, Foreign Key to Users)
- `Status` (string): Pending, Accepted, Rejected, Blocked
- `CreatedAt` (DateTime)
- `CreatedBy` (string, nullable)
- `UpdatedAt` (DateTime, nullable)
- `UpdatedBy` (string, nullable)
- `IsDeleted` (bool)

**Indexes:**
- Unique on (SenderId, ReceiverId)
- On SenderId
- On ReceiverId
- On Status

### Mention Table
- `Id` (Guid, Primary Key)
- `MentionedByUserId` (Guid, Foreign Key to Users)
- `MentionedUserId` (Guid, Foreign Key to Users)
- `EntityType` (string): Project, Task, Comment
- `EntityId` (Guid)
- `IsRead` (bool, default: false)
- `CreatedAt` (DateTime)
- `CreatedBy` (string, nullable)
- `UpdatedAt` (DateTime, nullable)
- `UpdatedBy` (string, nullable)
- `IsDeleted` (bool)

**Indexes:**
- On MentionedByUserId
- On MentionedUserId
- On EntityId
- Composite on (MentionedUserId, IsRead)

---

## Integration Notes

### Real-Time Notifications
The system automatically creates database notifications when:
- A friend request is sent (type: FriendRequestSent)
- A friend request is accepted (type: FriendRequestAccepted)
- A user is mentioned (type: MentionedInTask)

These notifications are stored in the `Notification` table and can be retrieved via the Notification API.

### Soft Delete Behavior
Both Friendship and Mention entities support soft delete. When deleted, the `IsDeleted` flag is set and entities are excluded from queries.

### User Navigation Properties
The User entity now has the following navigation properties:
- `FriendshipsSent` - Friend requests sent by this user
- `FriendshipsReceived` - Friend requests received by this user
- `MentionsCreated` - Mentions created by this user
- `MentionsReceived` - Mentions where this user is mentioned

---

## Implementation Details

### Services
- **FriendService** (`Miqat.Application.Services.FriendService`)
  - Implements: `IFriendService`
  - Handles all friend operations with business logic validation

- **MentionService** (`Miqat.Application.Services.MentionService`)
  - Implements: `IMentionService`
  - Handles mention parsing using regex pattern matching
  - Handles mention creation with duplicate prevention

### Controllers
- **FriendsController** (`Miqat.API.Controllers.FriendsController`)
  - Route: `api/friends`
  - Handles all friend-related HTTP requests

- **MentionsController** (`Miqat.API.Controllers.MentionsController`)
  - Route: `api/mentions`
  - Handles all mention-related HTTP requests

### Entity Configurations
- **FriendshipConfiguration** - EF Core entity type configuration for Friendship
- **MentionConfiguration** - EF Core entity type configuration for Mention

### Enumerations
- **FriendshipStatus** - Pending, Accepted, Rejected, Blocked
- **EntityType** - Project, Task, Comment

---

## Testing Recommendations

### Friend System Tests:
1. Send a friend request successfully
2. Prevent self-friend requests
3. Prevent duplicate friend requests
4. Accept a friend request
5. Reject a friend request
6. Block a user
7. Unblock a user
8. Get friends list (should only include accepted friendships)
9. Get pending requests
10. Get sent requests

### Mention System Tests:
1. Parse mentions from text with valid @username patterns
2. Handle multiple mentions in a single text
3. Create mentions and verify notifications are generated
4. Prevent self-mentions
5. Handle duplicate mentions in same entity
6. Mark mention as read
7. Get unread mentions
8. Get unread mentions count

---

## Error Handling

All endpoints return appropriate HTTP status codes:
- `200 OK` - Successful GET or operation completed
- `201 Created` - Resource created successfully
- `204 No Content` - Successful deletion
- `400 Bad Request` - Invalid input or business rule violation
- `401 Unauthorized` - Missing or invalid authentication token
- `403 Forbidden` - User not authorized for the resource
- `404 Not Found` - Resource not found
- `500 Internal Server Error` - Unexpected server error

---

## Future Enhancements

1. Add GraphQL queries for friend and mention data
2. Implement mention notifications via SignalR for real-time updates
3. Add friend request expiration (e.g., after 30 days)
4. Add @all and @group mention types
5. Add mention search functionality
6. Add friend request filtering and sorting
7. Add mutual friends endpoint
8. Add suggested friends based on common groups/tasks
