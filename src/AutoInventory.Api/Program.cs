using System.Text.Json.Serialization;
using AutoInventory.Api.Data;
using AutoInventory.Api.Endpoints;
using AutoInventory.Api.Rdw;
using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseMySQL(connectionString));

builder.Services.AddRdw(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<VehicleService>();
builder.Services.AddScoped<TestDriveService>();

var hoursSection = builder.Configuration.GetSection("OpeningHours");
builder.Services.AddSingleton(OpeningHours.Parse(
    hoursSection["TimeZone"] ?? "Europe/Amsterdam",
    hoursSection.GetSection("Days").GetChildren().ToDictionary(d => d.Key, d => d.Value ?? "")));

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("AutoInventory API"));

app.MapVehicleEndpoints();
app.MapTestDriveEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.Run();

public partial class Program;
