var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "AutoInventory");

app.Run();

public partial class Program;
