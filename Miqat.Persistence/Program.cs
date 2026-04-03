using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

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
});

builder.Services.AddAuthorization();

// ── Repositories & UoW ───────────────────────────────────────────────────────
builder.Services.AddScoped(typeof(IGenericRepository<>),
    typeof(GenericRepository<>));
builder.Services.AddScoped<IUnitOfWork,
    Miqat.infrastructure.persistence.UnitOfWork.UnitOfWork>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEmailService, EmailService>();

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
builder.Services.AddScoped<SeederRunner>();

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
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

// ── Seeder ────────────────────────────────────────────────────────────────────
try                                                          // ✅ wrapped
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<SeederRunner>();
    await seeder.RunAllAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"[Seeder] ❌ Failed: {ex.Message}");  // ✅ won't crash app
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<GlobalExceptionMiddleware>();  // ✅ moved after HTTPS
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();