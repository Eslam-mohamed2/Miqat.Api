using Microsoft.AspNetCore.Connections;
using Miqat.infrastructure.persistence.Data;
using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.infrastructure.persistence.UnitOfWork;
using Miqat.infrastructure.persistence.Repositories.GenericRepository;
using Miqat.Application.Services;
using Miqat.Application.Common;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("defaultConnection");

builder.Services.AddDbContext<MiqatDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IUnitOfWork,UnitOfWork>();
builder.Services.AddScoped<UserMapper>();
builder.Services.AddScoped<TaskMapper>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITaskService, TaskService>();


var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
