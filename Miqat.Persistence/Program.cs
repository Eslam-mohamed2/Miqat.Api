using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Miqat.API.Background;
using Miqat.API.Controllers;
using Miqat.API.Middleware;
using Miqat.API.Swagger;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Services;
using Miqat.Application.Validators;
using Miqat.infrastructure.persistence.Data;
using Miqat.infrastructure.persistence.Data.Seeds;
using Miqat.infrastructure.persistence.Repositories.GenericRepository;
using Miqat.infrastructure.persistence.Services;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<MiqatDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("Miqat.infrastructure.persistence")));

// ── JWT Settings ──────────────────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings"));

var jwtSettings = builder.Configuration
    .GetSection("JwtSettings").Get<JwtSettings>()!;

// ── JWT Authentication ────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero
    };

    // Browsers cannot attach an Authorization header to a WebSocket, so the
    // SignalR client sends the JWT as ?access_token=... — accepted for hub
    // paths only, never for the REST surface.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) &&
                context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return System.Threading.Tasks.Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
// CORS with AllowCredentials is an auth boundary, so localhost only belongs in
// it while developing — a production API that trusts http://localhost lets any
// site running on a visitor's own machine ride their cookies. Deployed origins
// can be overridden per environment via Cors:AllowedOrigins without a rebuild.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "https://mqiatsmartcalendar.vercel.app",
        "https://miqatsmartcalendar.vercel.app",
        "https://miqat.vercel.app",
        "https://mqiat-git-main-eslams-projects-b9cff232.vercel.app"
    };

if (builder.Environment.IsDevelopment())
{
    allowedOrigins = allowedOrigins
        .Concat(new[] { "http://localhost:3000", "http://localhost:4200", "http://localhost:4208", "https://localhost:7000" })
        .ToArray();
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});
// ── Repositories & UoW ───────────────────────────────────────────────────────
builder.Services.AddScoped(typeof(IGenericRepository<>),
    typeof(GenericRepository<>));
builder.Services.AddScoped<IUnitOfWork,
    Miqat.infrastructure.persistence.UnitOfWork.UnitOfWork>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
// Who is calling, and what they are allowed to touch. Registered before the
// services that depend on them.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSignalR(options =>
    {
        // Room for the set-up messages of a video call (the default is 32 KB).
        options.MaximumReceiveMessageSize = 128 * 1024;
    })
    // Enums by name in live events too, as in the REST answers.
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<IRealtimeNotifier, Miqat.API.Hubs.SignalRRealtimeNotifier>();
builder.Services.AddScoped<IAccessPolicy, AccessPolicy>();

builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IMentionService, MentionService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IUserOverviewService, UserOverviewService>();

// Calendar: calendars and preferences, events and invitations, the feed,
// .ics files, finding free time.
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<ICalendarFeedService, CalendarFeedService>();
builder.Services.AddScoped<ICalendarFileService, CalendarFileService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IEventAttendeeService, EventAttendeeService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();

// What hangs off a task or an event, and the checklist and links of a task.
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<ISubtaskService, SubtaskService>();
builder.Services.AddScoped<ITaskDependencyService, TaskDependencyService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();

// Chats between friends, and who has the app open (kept in memory, see PresenceTracker).
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddSingleton<IPresenceTracker, Miqat.API.Hubs.PresenceTracker>();

// Voice and video calls: the calls in progress are kept in memory too.
builder.Services.AddScoped<ICallService, CallService>();
builder.Services.AddSingleton<CallRegistry>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Calls").Get<CallSettings>() ?? new CallSettings());

// Shared by several modules: who is connected to whom, and the one place
// notifications and live updates are sent from.
builder.Services.AddScoped<PeopleDirectory>();
builder.Services.AddScoped<INotificationSender, NotificationSender>();
builder.Services.AddScoped<TaskBroadcaster>();
builder.Services.AddScoped<EventAnnouncer>();

// Notifications to browsers that are closed (Web Push). Does nothing until
// Push:PublicKey / Push:PrivateKey are set.
builder.Services.AddHttpClient<IPushSender, WebPushSender>(client => client.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddSingleton(
    builder.Configuration.GetSection("Notifications").Get<NotificationSettings>() ?? new NotificationSettings());
builder.Services.AddSingleton(
    builder.Configuration.GetSection("WeeklySummary").Get<WeeklySummarySettings>() ?? new WeeklySummarySettings());

// ── Background work ───────────────────────────────────────────────────────────
// Emails and device notifications leave the request through this queue, so a
// slow mail provider never makes a click wait.
builder.Services.AddSingleton<BackgroundQueue>();
builder.Services.AddSingleton<IBackgroundQueue>(services => services.GetRequiredService<BackgroundQueue>());
builder.Services.AddHostedService(services => services.GetRequiredService<BackgroundQueue>());

// Reminders and the weekly summary email, each on its own timer.
builder.Services.AddScoped<ReminderDispatcher>();
builder.Services.AddScoped<WeeklySummaryService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddHostedService<WeeklySummaryWorker>();

// /api/Task/{id}/reminders and /api/Event/{id}/reminders are one action each.
builder.Services.Configure<RouteOptions>(options =>
    options.ConstraintMap[ItemKindRouteConstraint.Name] = typeof(ItemKindRouteConstraint));

// ── Smaller answers ───────────────────────────────────────────────────────────
// JSON compresses to roughly a tenth of its size, which matters far more than
// server time once the client is on a phone connection.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("text/calendar");
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// ── Proxy awareness ───────────────────────────────────────────────────────────
// Hosted behind a platform proxy (Azure, Render, ...), every request appears to
// come from the proxy. Reading X-Forwarded-For restores the real client address,
// which the rate limiter and the refresh-token audit columns both rely on.
// ForwardLimit = 1 takes only the entry added by the nearest proxy — the one
// value in that header a client cannot forge.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Email Settings ────────────────────────────────────────────────────────────
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

// ── Mappers ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<TaskMapper>();
builder.Services.AddScoped<TaskDtoFactory>();
builder.Services.AddScoped<UserMapper>();
builder.Services.AddScoped<GroupMapper>();
builder.Services.AddScoped<NotificationMapper>();

// ── Seeders ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<UserSeeder>();
builder.Services.AddScoped<GroupSeeder>();
builder.Services.AddScoped<TaskSeeder>();
builder.Services.AddScoped<NotificationSeeder>();
builder.Services.AddScoped<FriendshipSeeder>();
builder.Services.AddScoped<MentionSeeder>();
builder.Services.AddScoped<SeederRunner>();
builder.Services.AddScoped<DemoAccountSeeder>();
builder.Services.AddScoped<CalendarDemoSeeder>();
builder.Services.AddScoped<ChatDemoSeeder>();

// ── Controllers + Swagger ─────────────────────────────────────────────────────
// ── Rate limiting ────────────────────────────────────────────────────────────
// The auth surface had none: OtpCode has no attempt counter and login has no
// lockout, so a 6-digit code with a 10-minute window was open to unlimited
// guessing. Partitioning by email (falling back to IP) means one account being
// attacked cannot lock out everyone else.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Too many attempts. Please wait a moment and try again.\",\"data\":null,\"errors\":null}",
            token);
    };

    // Limits come from configuration (RateLimiting:Otp / RateLimiting:Auth) so
    // they can be tuned per environment without a rebuild.
    var otpLimit = builder.Configuration.GetValue("RateLimiting:Otp:PermitLimit", 10);
    var otpWindow = builder.Configuration.GetValue("RateLimiting:Otp:WindowMinutes", 15);
    var authLimit = builder.Configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
    var authWindow = builder.Configuration.GetValue("RateLimiting:Auth:WindowMinutes", 5);

    // Codes and passwords: deliberately strict.
    options.AddPolicy("otp", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = otpLimit,
                Window = TimeSpan.FromMinutes(otpWindow)
            }));

    // Sign-in and account creation: enough headroom for a typo, not for a script.
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authLimit,
                Window = TimeSpan.FromMinutes(authWindow)
            }));

    // Chat messages: generous for a person typing, not for a script flooding a friend.
    var chatLimit = builder.Configuration.GetValue("RateLimiting:Chat:PermitLimit", 60);
    options.AddPolicy("chat", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = chatLimit,
                Window = TimeSpan.FromMinutes(1)
            }));

    static string AuthPartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Prefer the email in the payload so an attacker cannot exhaust a
        // victim's budget from another IP, and vice versa. "auth-email" is
        // filled in from the JSON body by the middleware further down; before
        // that existed this always fell through to the IP alone, which behind a
        // proxy meant every user shared one budget.
        var email = context.Request.Query["email"].ToString();
        if (string.IsNullOrWhiteSpace(email) &&
            context.Items.TryGetValue("auth-email", out var fromBody))
        {
            email = fromBody?.ToString() ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(email))
            return $"email:{email.Trim().ToLowerInvariant()}|ip:{ip}";

        // Signed-in calls (change-password) carry no email: budget per account.
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? $"ip:{ip}" : $"user:{userId}";
    }
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Every incoming date is normalised to UTC (PostgreSQL timestamptz rejects
    // anything else, which used to surface as a 500 for values without a "Z"),
    // and enums may be sent by name as well as by number.
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    options.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();

// Looks up real rows of the demo account for the Swagger examples (and only
// when Seed:DemoData is on - see SwaggerSampleProvider).
builder.Services.AddSingleton<SwaggerSampleProvider>();

builder.Services.AddSwaggerGen(c =>
{
    // Prefills every endpoint with example data: ids, query values and a
    // complete request body, so "Try it out" needs no typing.
    c.DocumentFilter<SwaggerExamplesFilter>();

    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Miqat API",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token here"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── Validation ────────────────────────────────────────────────────────────────
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Auto Migrate + Seeder (Smart Run) ─────────────────────────────────────────
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MiqatDbContext>();

        await db.Database.MigrateAsync();
        Console.WriteLine("[Migration] ✅ Database migrated successfully.");

        // Demo data is OFF unless explicitly switched on, and the switch is a
        // config value rather than the environment name — a container run with
        // ASPNETCORE_ENVIRONMENT unset defaults to Production, so keying off the
        // environment would be one missing variable away from seeding live.
        //
        // This matters more than "tidiness": SeedData/users.json creates an
        // Admin with a password committed to the repository, and
        // DemoAccountSeeder creates four more sign-in-able accounts sharing one
        // password. On a public deployment those are open doors, not clutter.
        var seedDemoData = app.Configuration.GetValue<bool>("Seed:DemoData");

        if (!seedDemoData)
        {
            Console.WriteLine("[Seeder] ⏭️  Demo data disabled (Seed:DemoData is not true). "
                            + "No seed accounts, projects or tasks will be created.");
        }
        else if (!await db.Users.AnyAsync())
        {
            Console.WriteLine("[Seeder] 🔄 Database empty. Running seeders...");
            var seeder = scope.ServiceProvider.GetRequiredService<SeederRunner>();
            await seeder.RunAllAsync();
            await scope.ServiceProvider.GetRequiredService<DemoAccountSeeder>().SeedAsync();
            Console.WriteLine("[Seeder] ✅ Data seeded successfully.");
        }
        else
        {
            // Idempotent, so it is safe to re-run against an already-seeded
            // development database.
            await scope.ServiceProvider.GetRequiredService<DemoAccountSeeder>().SeedAsync();
            Console.WriteLine("[Seeder] ℹ️ Demo world refreshed.");
        }

        // After the accounts exist, whichever branch created them. Idempotent too.
        if (seedDemoData)
        {
            await scope.ServiceProvider.GetRequiredService<CalendarDemoSeeder>().SeedAsync();
            await scope.ServiceProvider.GetRequiredService<ChatDemoSeeder>().SeedAsync();
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Migration/Seeder] ❌ Failed: {ex.Message}");
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
// First, so everything after it sees the real client address and scheme.
app.UseForwardedHeaders();

// Before anything that writes a response body.
app.UseResponseCompression();

// Swagger is a development and staging tool. It was mounted unconditionally,
// which published the entire API surface — every route, DTO and enum — to
// anonymous visitors in production. Opt in with Swagger:Enabled when a
// deployed environment genuinely needs it.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Miqat API v1");
        c.RoutePrefix = "swagger";

        // Each endpoint opens ready to run, and running login signs the page
        // in: the token goes onto every other request without copy and paste.
        c.EnableTryItOutByDefault();
        c.DisplayRequestDuration();
        c.UseRequestInterceptor(SwaggerUiScripts.RequestInterceptor);
        c.UseResponseInterceptor(SwaggerUiScripts.ResponseInterceptor);
    });
}

// ✅ CORS first — before all other middleware
app.UseCors("AllowFrontend");

// ✅ Single exception handler — removed duplicate app.UseExceptionHandler
app.UseMiddleware<GlobalExceptionMiddleware>();

// Hands the email in an auth request body to the rate limiter (see
// AuthPartitionKey). The body is buffered and rewound, so model binding still
// reads it normally afterwards.
app.Use(async (context, next) =>
{
    var request = context.Request;

    if (HttpMethods.IsPost(request.Method) &&
        request.Path.StartsWithSegments("/api/Auth", StringComparison.OrdinalIgnoreCase) &&
        request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true &&
        request.ContentLength is > 0 and <= 16 * 1024)
    {
        request.EnableBuffering();
        try
        {
            using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
            if (body.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in body.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "email", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        context.Items["auth-email"] = property.Value.GetString();
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON: model binding answers 400 a moment later.
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    await next();
});

// After authentication so signed-in calls can be limited per account.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHub<Miqat.API.Hubs.MiqatHub>("/hubs/miqat");

// Liveness probe for containers / uptime checks (no auth, no database).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.Run();