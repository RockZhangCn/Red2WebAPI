using Microsoft.EntityFrameworkCore;
using Red2WebAPI.Models;
using System.Text.Json; // Add this line
using System.Net.WebSockets; // Add this using directive

// var builder = WebApplication.CreateBuilder(args);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ApplicationName = typeof(Program).Assembly.FullName,
    ContentRootPath = Directory.GetCurrentDirectory(),
    EnvironmentName = Environments.Staging,
    WebRootPath = "customwwwroot"
});

Console.WriteLine($"Application Name: {builder.Environment.ApplicationName}");
Console.WriteLine($"Environment Name: {builder.Environment.EnvironmentName}");
Console.WriteLine($"ContentRoot Path: {builder.Environment.ContentRootPath}");
Console.WriteLine($"WebRootPath: {builder.Environment.WebRootPath}");


builder.Services.AddDbContext<TodoDb>(opt => opt.UseInMemoryDatabase("TodoList"));
builder.Services.AddDbContext<UserDb>(opt => opt.UseInMemoryDatabase("UserDb"));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowOrigin",
        builder => builder.WithOrigins("http://localhost:3000")
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials());
});

builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "Red2API";
    config.Title = "Red2API v1";
    config.Version = "v1";
});

builder.Services.AddDistributedMemoryCache(); // 使用内存缓存
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // 设置过期时间
    options.Cookie.HttpOnly = true; // 只能通过 HTTP 访问
    options.Cookie.IsEssential = true; // 在 GDPR 下的设置
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; // Use camelCase
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // Ignore case
    });

var app = builder.Build();

// Ensure CORS middleware is applied before other middleware
app.UseCors("AllowOrigin");

// app.UseFileServer();
// app.UseHsts();
app.UseWebSockets();
app.Logger.LogInformation("The app started");

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi(config =>
    {
        config.DocumentTitle = "TodoAPI";
        config.Path = "/swagger";
        config.DocumentPath = "/swagger/{documentName}/swagger.json";
        config.DocExpansion = "list";
    });
}

app.UseSession();
app.UseWebSockets();


app.MapGet("/", () => Results.Ok(new { Success=true, Message="Success."}));

app.MapPost("/logout", async (HttpContext httpContext, UserDb db) =>
{
    app.Logger.LogInformation("we get into logout here"); 
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var jsonData = JsonSerializer.Deserialize<JsonElement>(body);
  
    var email = jsonData.GetProperty("email").GetString();
    var nickname = jsonData.GetProperty("nickname").GetString();
    var avatar = jsonData.GetProperty("avatar").GetString();

    var exist = db.Users.FirstOrDefault(u => u.Email == email);

    var sessionEmail = httpContext.Session.GetString("UserId");
    app.Logger.LogInformation("we get session email " + sessionEmail);

    if (exist == null) {
        var userDto = new LoginUserDto
        {
            Message = "Not exist user, error occured.",
            Success = false,
        };

        return Results.Ok(userDto);
    } else {
        var userDto = new LoginUserDto
            {
                Email = email,
                Nickname = nickname,
                Avatar = avatar,
                Message = "Logout successful",
                Success = true,
            };       
       
        if (httpContext != null) {
            httpContext.Session.Clear(); 
        }
        return Results.Ok(userDto);
    }

});

app.MapPost("/register", async (Register user, UserDb db) =>
{
    var exist = db.Users.FirstOrDefault(u => u.Email == user.Email);
    if (exist != null) {
        return Results.Ok(new LoginUserDto
                            {
                                Email = user.Email,
                                Nickname = user.Nickname,
                                Avatar = user.Avatar,
                                Success = false,
                                Message = "Email already exists.",
                            });
    }

    db.Users.Add(user);
    app.Logger.LogInformation("New register user: " + JsonSerializer.Serialize(user));

    var userDto = new LoginUserDto
    {
        Email = user.Email,
        Nickname = user.Nickname,
        Avatar = user.Avatar,
        Success = true,
        Message = "Register successful.",
    };
    
    await db.SaveChangesAsync();
    return Results.Ok(userDto);
});

app.MapPost("/login", async (HttpContext httpContext, UserDb db) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var jsonData = JsonSerializer.Deserialize<JsonElement>(body);

    var email = jsonData.GetProperty("email").GetString();
    var password = jsonData.GetProperty("password").GetString();

    var user = db.Users.FirstOrDefault(u => u.Email == email);
    if (user != null && user.Password == password) {
        var userDto = new LoginUserDto
        {
            Email = user.Email,
            Nickname = user.Nickname,
            Avatar = user.Avatar, 
            Success = true, 
            Message = "Login successful.", 
        };
        
        httpContext.Session.SetString("UserId", user.Email);
        return Results.Ok(userDto);
    } else {
        var userDto = new LoginUserDto
        {
            Success = false, 
            Message = "Login failed.", 
        };
        return Results.Ok(userDto);
    }
});


// Game Playing.
// verify Seat and table.
app.Map("/ws_playing", async (HttpContext context, UserDb db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await Echo(context, webSocket);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});

// GamePanel broadcast data.
// Take a Seat
// Define a WebSocket handler
app.Map("/ws_hall", async (HttpContext context, UserDb db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await Echo(context, webSocket);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});

// WebSocket Echo method
static async Task Echo(HttpContext context, WebSocket webSocket)
{
    var buffer = new byte[1024 * 4];
    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

    while (!result.CloseStatus.HasValue)
    {
        // Echo the received message back to the client
        await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }

    await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
}

app.Run();