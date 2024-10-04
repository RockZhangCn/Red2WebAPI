using Microsoft.EntityFrameworkCore;
using Red2WebAPI.Models;
using System.Text.Json; // Add this line
using System.Net.WebSockets; // Add this using directive
using System.Text; // Added for Encoding
using Red2WebAPI.Seed;
using Red2WebAPI.Communication;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
// var builder = WebApplication.CreateBuilder(args);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ApplicationName = typeof(Program).Assembly.FullName,
    ContentRootPath = Directory.GetCurrentDirectory(),
    EnvironmentName = Environments.Staging,
});

Console.WriteLine($"Application Name: {builder.Environment.ApplicationName}");
Console.WriteLine($"Environment Name: {builder.Environment.EnvironmentName}");
Console.WriteLine($"ContentRoot Path: {builder.Environment.ContentRootPath}");
Console.WriteLine($"WebRootPath: {builder.Environment.WebRootPath}");

builder.Services.AddSqlite<UserDbContext>("Data Source=User.db");
builder.Services.AddDbContext<GameTableDbContext>(opt => opt.UseInMemoryDatabase("GameTable"));

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
    options.IdleTimeout = TimeSpan.FromMinutes(300); // 设置过期时间
    options.Cookie.HttpOnly = true; // 只能通过 HTTP 访问
    options.Cookie.IsEssential = true; // 在 GDPR 下的设置
});

builder.Services.AddLogging();
builder.Logging.AddConsole(); // Add this line to configure console logging

// // Add ILogger service to the DI container
// builder.Services.AddSingleton<ILogger<YourClassName>>(provider => 
//     provider.GetRequiredService<ILoggerFactory>().CreateLogger<YourClassName>());

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

app.MapPost("/logout", async (HttpContext httpContext, UserDbContext db) =>
{
    app.Logger.LogInformation("we get into logout here"); 
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var jsonData = JsonSerializer.Deserialize<JsonElement>(body);
  
    var email = "None";
    var nickname = "null";
    var avatar = 0;
    //TODO removed after client get correct logics.
    try {
        email = jsonData.GetProperty("email").GetString();
        nickname = jsonData.GetProperty("nickname").GetString();
        avatar = jsonData.GetProperty("avatar").GetInt32();
    } catch (KeyNotFoundException) {
        var userDto = new LoginUserDto
        {
            Message = "Not exist user, error occured.",
            Success = false,
        };

        return Results.Ok(userDto);
    }

    var exist = db.Users.FirstOrDefault(u => u.Email == email);

    // var sessionEmail = httpContext.Session.GetString("UserId");
    // app.Logger.LogInformation("we get session email " + sessionEmail);

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

app.MapPost("/register", async (User user, UserDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("Received registration request: {@User}", user); // Log the incoming user data

    var exist = db.Users.FirstOrDefault(u => u.Email == user.Email);
    if (exist != null) {
        logger.LogWarning("Registration failed: Email already exists for {Email}", user.Email);
        return Results.Ok(new LoginUserDto
        {
            Email = user.Email,
            Nickname = user.Nickname,
            Avatar = user.Avatar,
            Success = false,
            Message = "Server: Email already exists.",
        });
    }

    user.Salt = UserDbContext.GenerateRandomString();
    user.Digest = UserDbContext.CalcDigest(user.Password, user.Salt);
    user.Score = 0;

    db.Users.Add(user);
    logger.LogInformation("New register user: {@User}", user);

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



app.MapPost("/login", async (HttpContext httpContext, UserDbContext db) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var jsonData = JsonSerializer.Deserialize<JsonElement>(body);

    var email = jsonData.GetProperty("email").GetString();
    var password = jsonData.GetProperty("password").GetString();

    var user = db.Users.FirstOrDefault(u => u.Email == email);

    if (user != null) { 
        var userSalt = user.Salt;
        if (!string.IsNullOrEmpty(password) && user.Digest == UserDbContext.CalcDigest(password, userSalt)) {
            var userDtoSuccess = new LoginUserDto
            {
                Email = user.Email,
                Nickname = user.Nickname,
                Avatar = user.Avatar, 
                Success = true, 
                Message = "Login successful.", 
            };
            
            httpContext.Session.SetInt32("UserId", user.Id);
            return Results.Ok(userDtoSuccess);
        }
    } 
    
    var userDtoFailed = new LoginUserDto
    {
        Success = false, 
        Message = "Login failed.", 
    };
    return Results.Ok(userDtoFailed);
    
});


// Game Playing.
// verify Seat and table.
app.Map("/ws_playing", async (HttpContext context, GameTableDbContext db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await BroadCastHallData(app, context, webSocket, db);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});

// GamePanel broadcast data.
// Take a Seat
// Define a WebSocket handler
app.Map("/ws_hall", async (HttpContext context, GameTableDbContext db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        app.Logger.LogInformation("Current table size " + await db.Tables.CountAsync());
        
        ActiveWebSockets.AddSocket(context.Session.GetInt32("UserId")?? 0, webSocket);

        await BroadCastHallData(app, context, webSocket, db);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});



static async Task BroadCastHallData(WebApplication app, HttpContext context, 
                                    WebSocket webSocket, GameTableDbContext gameTableDb)
{
    SemaphoreSlim semaphore = new SemaphoreSlim(1, 1); 
    List<WebSocket> allPlayers = new List<WebSocket>();

    // Periodic data sending
    var cancellationTokenSource = new CancellationTokenSource();

    _ = Task.Run(async () =>
    {
        while (!cancellationTokenSource.Token.IsCancellationRequested)
        {
            await semaphore.WaitAsync();
            var data = await gameTableDb.Tables
            .Select(table => new
            {
                tableIdx = table.TableId, // Assuming TableId is the identifier for the table
                tableUsers = table.Players.Select(player => new
                {
                    pos = player.Pos,
                    avatar = player.AvatarId, // Assuming AvatarId is the property for avatar
                    nickname = player.Nickname
                }).ToArray()
            }).ToArrayAsync();
           
            var jsonObject = new
            {
                Type = "BroadCast",
                Data = data // Use the object array here
            };

            // Serialize the object to a JSON string
            string jsonString = JsonSerializer.Serialize(jsonObject);

            var messageBuffer = Encoding.UTF8.GetBytes(jsonString);
            await webSocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    });

    var buffer = new byte[1024 * 4];
    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    
    while (!result.CloseStatus.HasValue)
    {
        // Print the received content
        var receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
        app.Logger.LogInformation($"Received client message: {receivedMessage}"); // Log the received message
        // "TAKESEAT"

        var clientMessage = JsonSerializer.Deserialize<ClientMessage>(receivedMessage);

        var replyResult = false;
        var replyMsg = "";
        string replyJsonString;
        if (clientMessage != null && clientMessage.Action == "TAKESEAT") {
            var tableIdx = clientMessage.TableIdx;
            var pos = clientMessage.Pos;
            var table = await gameTableDb.Tables.FirstOrDefaultAsync(table => table.TableId == tableIdx);
            if (table != null) {
                var player = table.Players.FirstOrDefault(p => p.Pos == pos); // Changed to use FirstOrDefault

                if (player == null) {
                    var id = context.Session.GetInt32("UserId");
                    var newPlayer = new Player{
                        UserId = id ?? -1,
                    
                        AvatarId = clientMessage.Avatar,
                        Nickname =  clientMessage.NickName,
                        TableId =  tableIdx,
                        Pos = pos,
                        GameTable = table,
                    };

                    table.Players.Add(newPlayer);
                    gameTableDb.SaveChanges();
                    semaphore.Release();
                    replyResult = true;

                } else {
                    replyResult = false;
                    replyMsg = "Alread seated.";
                }

            } else {
                replyResult = false;
                replyMsg = "Invalid Table.";
            }

            var replyObj = new ServerMessage{
                Type = "REPLY",
                Result = replyResult,
                Message = replyMsg,
            };

            replyJsonString = JsonSerializer.Serialize(replyObj);
        } else {
            replyJsonString = "Ts";
        }
         // Send the replyJsonString back to the client
        var replyMessageBuffer = Encoding.UTF8.GetBytes(replyJsonString);
        app.Logger.LogInformation("We reply to client message " + replyJsonString);

        await webSocket.SendAsync(new ArraySegment<byte>(replyMessageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            
        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }
}

// New async method to run a loop
static async Task RunAsyncLoop(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        // Your loop logic here
        
        await Task.Delay(1000); // Example: wait for 1 second
    }
}

// In your existing code, invoke the async loop
var cancellationTokenSource = new CancellationTokenSource();
_ = RunAsyncLoop(cancellationTokenSource.Token); // Start the async loop

Extensions.CreateDbIfNotExists(app);

app.Run();