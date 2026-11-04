using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AutoInventory.Api.Data;
using AutoInventory.Api.Endpoints;
using AutoInventory.Api.Rdw;
using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, cfg) => cfg
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

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

    builder.Services.AddRazorPages();
    builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
    builder.Services.AddOpenApi();

    var app = builder.Build();

    static bool IsApi(HttpContext c) => c.Request.Path.StartsWithSegments("/api");
    app.UseWhen(IsApi, api =>
    {
        api.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
        });
        api.UseStatusCodePages();
    });
    app.UseWhen(c => !IsApi(c), web =>
    {
        web.UseExceptionHandler("/Error");
        web.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
    });

    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
        SupportedCultures = [CultureInfo.InvariantCulture],
        SupportedUICultures = [CultureInfo.InvariantCulture],
        RequestCultureProviders = [],
    });
    app.UseSerilogRequestLogging();

    app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
        }
    });

    app.UseStaticFiles();

    // Must come after the error handling, otherwise the /Error re-execution finds no page.
    app.UseRouting();

    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("AutoInventory API"));

    app.MapRazorPages();
    app.MapVehicleEndpoints();
    app.MapTestDriveEndpoints();

    if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    app.Run();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
