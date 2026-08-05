using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Miqat.API.Middleware;
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
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, Miqat.API.Hubs.SignalRRealtimeNotifier>();
builder.Services.AddScoped<IAccessPolicy, AccessPolicy>();

builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IMentionService, MentionService>();

// ── Email Settings ────────────────────────────────────────────────────────────
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

// ── Mappers ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<TaskMapper>();
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

    // Codes and passwords: deliberately strict.
    options.AddPolicy("otp", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15)
            }));

    // Sign-in and account creation: enough headroom for a typo, not for a script.
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5)
            }));

    static string AuthPartitionKey(HttpContext context)
    {
        // Prefer the email in the payload so an attacker cannot exhaust a
        // victim's budget from another IP, and vice versa.
        var email = context.Request.Query["email"].ToString();
        if (string.IsNullOrWhiteSpace(email) &&
            context.Items.TryGetValue("auth-email", out var fromBody))
        {
            email = fromBody?.ToString() ?? string.Empty;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return string.IsNullOrWhiteSpace(email) ? $"ip:{ip}" : $"email:{email.ToLowerInvariant()}|ip:{ip}";
    }
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
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
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Migration/Seeder] ❌ Failed: {ex.Message}");
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
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
    });
}

// ✅ CORS first — before all other middleware
app.UseCors("AllowFrontend");

// ✅ Single exception handler — removed duplicate app.UseExceptionHandler
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<Miqat.API.Hubs.MiqatHub>("/hubs/miqat");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run();