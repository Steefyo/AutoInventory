using AutoInventory.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseMySQL(connectionString));

var app = builder.Build();

app.MapGet("/", () => "AutoInventory");

// Off unless configured (appsettings.Development.json turns it on): in production, migrations belong
// in the deployment pipeline, not in every app instance racing to migrate at startup.
if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.Run();

public partial class Program;
