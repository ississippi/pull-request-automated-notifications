using NotificationsService.Services;
using Microsoft.Extensions.Logging;


//var builder = WebApplication.CreateBuilder();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "wwwroot",
    ApplicationName = typeof(Program).Assembly.FullName,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(System.Net.IPAddress.Any, 5000); // explicitly IPv4
});


// Add services to the container.
builder.Services.AddSingleton<PrService>();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Automatically loads appsettings.json and appsettings.<Environment>.json
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.UseWebSockets();
app.UseStaticFiles(); // makes anything in /wwwroot automatically available at the root URL.

// WebSocket endpoint
app.Map("/ws/prs", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync();
        var prService = context.RequestServices.GetRequiredService<PrService>();
        await prService.HandleWebSocketAsync(ws);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.MapGet("/health", (ILogger<Program> logger) =>
{
    //logger.LogInformation("Health check received at {Time}", DateTime.UtcNow);
    return Results.Ok("Healthy");
});

foreach (var address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
{
    Console.WriteLine($"Bound to IP: {address}");
}

app.Run();


