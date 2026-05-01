# Architecture Overview

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CLIENT APPLICATION                               │
│                                                                             │
│  Mobile/Web App - Friends UI          Mobile/Web App - Mentions UI        │
└─────────────────────────────────────────────────────────────────────────────┘
									↓ HTTP/REST
┌─────────────────────────────────────────────────────────────────────────────┐
│                         API GATEWAY / CORS MIDDLEWARE                       │
└─────────────────────────────────────────────────────────────────────────────┘
									↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                        AUTHENTICATION LAYER (JWT)                           │
│                                                                             │
│  Bearer Token Validation → ClaimsPrincipal → GetCurrentUserId()           │
└─────────────────────────────────────────────────────────────────────────────┘
									↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CONTROLLER LAYER                                  │
│                                                                             │
│  ┌────────────────────────┐        ┌────────────────────────┐            │
│  │  FriendsController     │        │ MentionsController     │            │
│  │                        │        │                        │            │
│  │ POST /send-request     │        │ GET /mentions          │            │
│  │ PUT /accept/{id}       │        │ GET /unread            │            │
│  │ PUT /reject/{id}       │        │ PUT /{id}/read         │            │
│  │ PUT /block/{id}        │        │ POST /parse            │            │
│  │ PUT /unblock/{id}      │        │ POST /create           │            │
│  │ GET /                  │        │                        │            │
│  │ GET /pending           │        │                        │            │
│  │ GET /sent              │        │                        │            │
│  └────────────────────────┘        └────────────────────────┘            │
└─────────────────────────────────────────────────────────────────────────────┘
						↓                              ↓
┌───────────────────────────────────┐  ┌──────────────────────────────────────┐
│    FRIEND SERVICE LAYER           │  │   MENTION SERVICE LAYER              │
│                                   │  │                                      │
│  FriendService                    │  │  MentionService                      │
│  ├─ SendFriendRequest()           │  │  ├─ GetMentions()                   │
│  ├─ AcceptRequest()               │  │  ├─ GetUnreadMentions()             │
│  ├─ RejectRequest()               │  │  ├─ MarkAsRead()                    │
│  ├─ BlockUser()                   │  │  ├─ ParseMentionsFromText()         │
│  ├─ UnblockUser()                 │  │  │   (Regex: @(\w+))                │
│  ├─ GetFriends()                  │  │  └─ CreateMentions()                │
│  ├─ GetPendingRequests()          │  │                                      │
│  └─ GetSentRequests()             │  │  Dependencies:                       │
│                                   │  │  - IUnitOfWork                       │
│  Dependencies:                    │  │  - INotificationService              │
│  - IUnitOfWork                    │  │                                      │
│  - INotificationService           │  │                                      │
└───────────────────────────────────┘  └──────────────────────────────────────┘
			↓                                          ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         UNIT OF WORK / REPOSITORY                           │
│                                                                             │
│  IGenericRepository<Friendship>      IGenericRepository<Mention>           │
│  ├─ GetByIdAsync()                   ├─ GetByIdAsync()                    │
│  ├─ FindAsync()                      ├─ FindAsync()                       │
│  ├─ AddAsync()                       ├─ AddAsync()                        │
│  ├─ Update()                         ├─ Update()                          │
│  └─ DeleteAsync()                    └─ DeleteAsync()                     │
│                                                                             │
│  INotificationService                                                       │
│  └─ Creates Notification records automatically                              │
└─────────────────────────────────────────────────────────────────────────────┘
			↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                        ENTITY FRAMEWORK CORE                                │
│                                                                             │
│  MiqatDbContext                                                             │
│  ├─ DbSet<Friendship>              (with Configuration)                    │
│  ├─ DbSet<Mention>                 (with Configuration)                    │
│  ├─ DbSet<User>                    (with navigation properties)            │
│  ├─ DbSet<Notification>                                                    │
│  └─ ... other entities                                                     │
│                                                                             │
│  Configurations:                                                            │
│  ├─ FriendshipConfiguration                                                │
│  │  └─ Relationships, Indexes, Cascades                                    │
│  └─ MentionConfiguration                                                   │
│     └─ Relationships, Indexes, Cascades                                    │
└─────────────────────────────────────────────────────────────────────────────┘
			↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         DATABASE (PostgreSQL)                               │
│                                                                             │
│  ┌──────────────────┐      ┌──────────────────┐    ┌──────────────────┐   │
│  │  Users Table     │      │ Friendships Tbl  │    │  Mentions Table  │   │
│  │                  │◄─────┤                  │    │                  │   │
│  │ - Id (PK)        │      │ - Id (PK)        │    │ - Id (PK)        │   │
│  │ - FullName       │◄─────┤ - SenderId (FK)  │    │ - MentionedBy    │   │
│  │ - Email          │      │ - ReceiverId(FK) │    │   UserId (FK)    │   │
│  │ - ... other cols │      │ - Status         │    │ - MentionedUser  │   │
│  │                  │      │ - CreatedAt      │    │   Id (FK)        │   │
│  │ Indexes:         │      │ - UpdatedAt      │    │ - EntityType     │   │
│  │ - Email (UNIQUE) │      │ - IsDeleted      │    │ - EntityId       │   │
│  │                  │      │                  │    │ - IsRead         │   │
│  │ Navigation:      │      │ Indexes:         │    │ - CreatedAt      │   │
│  │ - FriendshipsSent│      │ - (Sender, Recv) │    │ - IsDeleted      │   │
│  │ - Friendships    │      │   UNIQUE         │    │                  │   │
│  │   Received       │      │ - SenderId       │    │ Indexes:         │   │
│  │ - MentionsCreate │      │ - ReceiverId     │    │ - MentionedUserId│   │
│  │ - Mentions       │      │ - Status         │    │ - EntityId       │   │
│  │   Received       │      │                  │    │ - (UserId, Read) │   │
│  │                  │      │ Cascades:        │    │                  │   │
│  │                  │      │ - Delete on User │    │ Cascades:        │   │
│  │                  │      │   delete         │    │ - Delete on User │   │
│  │                  │      │                  │    │   delete         │   │
│  └──────────────────┘      └──────────────────┘    └──────────────────┘   │
│                                                                             │
│  ┌──────────────────┐                                                      │
│  │ Notification Tbl │ ◄─── Created by Friend & Mention Services           │
│  │ - RecipientUserId│                                                      │
│  │ - Type           │                                                      │
│  │ - LinkedEntityId │                                                      │
│  └──────────────────┘                                                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Data Flow Diagrams

### Friend Request Flow

```
USER A                          SYSTEM                         USER B

  │                                                            │
  ├─ POST /api/friends/                                       │
  │  send-request/{userBId}                                   │
  │                                                            │
  └──────────────────────────→ FriendsController              │
								│                             │
								├─ GetCurrentUserId()         │
								├─ Validate(userA ≠ userB)    │
								├─ Check for duplicates       │
								│                             │
								└─→ FriendService             │
									.SendFriendRequest()      │
									│                         │
									├─ Create Friendship      │
									│  (Status: Pending)      │
									│                         │
									├─→ IUnitOfWork           │
									│   .Repository.AddAsync() │
									│                         │
									└─→ Create Notification ◄─┐
										for UserB            │
										│                    │
										├─→ Database         │
										└────────────────────→ NOTIFICATION
															 │
  ◄─ Response:                                              │
  │  Friendship Created                                     │
  └──────────────────────────────────────────────────────────→ UserB sees
															  pending request
															  GET /pending
```

### Mention Flow

```
USER A                          SYSTEM                         USER B

  │                                                            │
  ├─ POST /api/mentions/create                               │
  │  { text: "@john check this" }                            │
  │                                                            │
  └──────────────────────────→ MentionController             │
								│                             │
								├─ ParseMentions()           │
								│  Regex: @(\w+)             │
								│  Find: "john"              │
								│  Lookup: User by email/name│
								│                             │
								└─→ MentionService            │
									.CreateMentions()        │
									│                         │
									├─ For each mentioned user│
									│  ├─ Validate ≠ self    │
									│  ├─ Check duplicates    │
									│  ├─ Create Mention      │
									│  │  (IsRead: false)     │
									│  │                      │
									│  └─→ Create Notification┐
									│      for User B        │ │
									│                        │ │
									├─→ IUnitOfWork         │ │
									│   .Repository.AddAsync()│ │
									│                        │ │
									└─→ Database            │ │
										Mention Record      │ │
										Notification        │ │
														   ◄─┘
															│
  ◄─ Response:                                              │
  │  Mentions Created                                       │
  └──────────────────────────────────────────────────────────→ UserB sees
															  unread mention
															  GET /unread
```

---

## State Diagrams

### Friendship States

```
					┌──────────────┐
					│   PENDING    │
					└──────────────┘
					/              \
				   /                \
	[SendRequest]                    [SendRequest]
				/                      \
			   /                        \
		 ┌──────────┐             ┌──────────┐
		 │ ACCEPTED │             │ REJECTED │
		 └──────────┘             └──────────┘
			  │
			  │ [Block]
			  ▼
		 ┌──────────┐
		 │ BLOCKED  │
		 └──────────┘

State Transitions:
- Pending → Accepted: AcceptRequest (by Receiver)
- Pending → Rejected: RejectRequest (by Receiver)
- Any State → Blocked: BlockUser
- Blocked → (Soft Delete): UnblockUser
```

### Mention States

```
		 ┌─────────────┐
		 │  UNREAD     │ ◄──── Created with IsRead=false
		 └─────────────┘
			  │
			  │ [MarkAsRead]
			  ▼
		 ┌─────────────┐
		 │    READ     │
		 └─────────────┘
			  │
			  │ [Delete/SoftDelete]
			  ▼
		 ┌─────────────┐
		 │   DELETED   │ (IsDeleted=true, excluded from queries)
		 └─────────────┘
```

---

## Relationship Diagrams

### Friendship Relationships

```
┌─────────────────────────────────┐
│          USER                   │
│                                 │
│  - Id                           │
│  - FullName                     │
│  - Email                        │
│  - ProfilePictureUrl            │
│                                 │
│  Navigation:                    ��
│  - FriendshipsSent ─────┐       │
│  - FriendshipsReceived ┐└────┐  │
│  - MentionsCreated ────┐└───┐│  │
│  - MentionsReceived ──┐└───┐││  │
└─────────────────────────────────┘
		│         ▲              ▲
		│         │              │
   1:N  │         │ 1:N      1:N │
		│         │              │
   SenderId   ReceiverId         │
		│         │              │
		▼         │              │
┌──────────────────────────────┐ │
│      FRIENDSHIP              │ │
│                              │ │
│  - Id                        │ │
│  - SenderId (FK) ────→ USER  │ │
│  - ReceiverId (FK) ──→ USER  │ │
│  - Status                    │ │
│  - CreatedAt                 │ │
│  - UpdatedAt                 │ │
│  - IsDeleted                 │ │
│                              │ │
│  (Unique: SenderId,          │ │
│   ReceiverId)                │ │
└──────────────────────────────┘ │
		▲         │              │
		│         │ MentionedByUserId
		│         │ MentionedUserId
		│         ▼
		│    ┌──────────────────────────────┐
		│    │       MENTION                │
		│    │                              │
		│    │  - Id                        │
		│    │  - MentionedByUserId (FK)────┘
		│    │  - MentionedUserId (FK) ─────┘
		│    │  - EntityType (Task|Project)
		│    │  - EntityId
		│    │  - IsRead
		│    │  - CreatedAt
		│    │  - IsDeleted
		│    │                              │
		│    │  (Unique: MentionedBy,       │
		│    │   MentionedUser, Entity)     │
		│    └──────────────────────────────┘
		│
		└─→ NOTIFICATION (created automatically)
			- RecipientUserId (FK) → USER
			- TriggeredByUserId (FK) → USER
			- LinkedEntityId
			- LinkedEntityType
```

---

## Notification Creation Flow

```
When Friend Request is sent:
┌────────────────────────────────────────────────────────┐
│ Notification created with:                             │
│  - Title: "New Friend Request"                        │
│  - Message: "{Sender.FullName} sent you a request"   │
│  - Type: FriendRequestSent                            │
│  - RecipientUserId: Receiver                          │
│  - TriggeredByUserId: Sender                          │
│  - LinkedEntityId: Friendship.Id                      │
│  - LinkedEntityType: "Friendship"                     │
└────────────────────────────────────────────────────────┘

When Friend Request is accepted:
┌────────────────────────────────────────────────────────┐
│ Notification created with:                             │
│  - Title: "Friend Request Accepted"                   │
│  - Message: "{Receiver.FullName} accepted your req"  │
│  - Type: FriendRequestAccepted                        │
│  - RecipientUserId: Sender                            │
│  - TriggeredByUserId: Receiver                        │
│  - LinkedEntityId: Friendship.Id                      │
│  - LinkedEntityType: "Friendship"                     │
└────────────────────────────────────────────────────────┘

When User is mentioned:
┌────────────────────────────────────────────────────────┐
│ Notification created with:                             │
│  - Title: "You were mentioned"                        │
│  - Message: "{Mentioner} mentioned you in a {Type}"  │
│  - Type: MentionedInTask                              │
│  - RecipientUserId: MentionedUser                     │
│  - TriggeredByUserId: MentioningUser                  │
│  - LinkedEntityId: EntityId (Task/Project/Comment)   │
│  - LinkedEntityType: EntityType.ToString()            │
└────────────────────────────────────────────────────────┘

All notifications are retrievable via:
GET /api/notifications
GET /api/notifications/unread
```

---

## Concurrency & Locking Considerations

```
The system uses optimistic concurrency:

┌─────────────────────────────────────────────────────┐
│ User A tries to accept friendship with User B       │
│                                                     │
│  1. Fetch Friendship (Status: Pending)              │
│  2. User B accepts first (changes Status)           │
│  3. User A's update throws DbUpdateConcurrency      │
│     Exception (LastUpdateWins strategy)             │
│                                                     │
│ Solution: Client should refetch and retry           │
└─────────────────────────────────────────────────────┘

All BaseEntity updates automatically set:
- UpdatedAt: Current UTC DateTime
- UpdatedBy: UpdatedBy parameter (if provided)
```

---

## Performance Considerations

```
Indexes created for optimal query performance:

FRIENDSHIPS Table:
├─ (SenderId, ReceiverId) UNIQUE
│  └─ Prevents duplicate friendships
├─ SenderId
│  └─ Quick lookup of requests sent
├─ ReceiverId
│  └─ Quick lookup of requests received
└─ Status
   └─ Filter by state (Accepted, Pending, etc.)

MENTIONS Table:
├─ MentionedUserId + IsRead (composite)
│  └─ Efficient unread mentions query
├─ MentionedByUserId
│  └─ Find mentions created by user
├─ EntityId
│  └─ Find mentions for specific entity
└─ CreatedAt (implicit)
   └─ Sort by recency

Query Optimization Tips:
1. Load related Users when fetching Friendships
2. Use FindAsync with predicate instead of loading all
3. Apply filters before materializing results
4. Consider caching friend lists for active users
```
