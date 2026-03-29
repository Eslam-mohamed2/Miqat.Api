using Microsoft.AspNetCore.Authentication.JwtBearer;
using Miqat.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Miqat.Application.Interfaces;
using Miqat.Application.Services;
using Miqat.infrastructure.persistence.Data;
using Miqat.infrastructure.persistence.Data.Seeds;
using Miqat.infrastructure.persistence.Repositories.GenericRepository;
using Miqat.infrastructure.persistence.Services;
using System.Text;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MiqatDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ── JWT Settings ─────────────────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings"));

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSettings = builder.Configuration
    .GetSection("JwtSettings").Get<JwtSettings>()!;

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

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IUserService, UserService>();

// ── Mappers ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<TaskMapper>();
builder.Services.AddScoped<UserMapper>();
// ── Seeders ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<UserSeeder>();
builder.Services.AddScoped<GroupSeeder>();
builder.Services.AddScoped<TaskSeeder>();
builder.Services.AddScoped<NotificationSeeder>();
builder.Services.AddScoped<SeederRunner>();

// ── Controllers + Swagger ────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider
        .GetRequiredService<SeederRunner>();
    await seeder.RunAllAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
