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

app.MapPost("/logout", async (HttpContext httpContext, UserDbContext db, 
                                        GameTableDbContext gameTableDb) =>
{
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
            Message = "No exist user, error occured.",
            Success = false,
        };

        return Results.Ok(userDto);
    }

    var exist = db.Users.FirstOrDefault(u => u.Email == email);

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
            ActiveWebSockets.RemoveSocket(httpContext.Session.GetInt32("UserId")?? 0);
            
            // clear your position in database.
            var userTable = httpContext.Session.GetInt32("UserTableId");
            var userPos = httpContext.Session.GetInt32("UserTablePos");
            if (userTable.HasValue && userPos.HasValue) {
                var userId = httpContext.Session.GetInt32("UserId");
                var oldTable = await gameTableDb.Tables.FirstOrDefaultAsync(table => table.TableId == userTable);

                if (oldTable != null) {
                    // Ensure the players are fetched from the database after any changes
                    await gameTableDb.Entry(oldTable).Collection(t => t.Players).LoadAsync(); // Load players again
                }
                // Check if table is not null before accessing Players
                var oldPlayer = oldTable?.Players?.Find(p => p.Pos == userPos);
                
                if (oldPlayer != null) {
                    app.Logger.LogInformation("We clear position " + userTable + "" + userPos);
                    oldTable?.Players?.Remove(oldPlayer);
                }
            }
            // clear End.
            app.Logger.LogInformation("We logout now");

            gameTableDb.SaveChanges();

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
            httpContext.Session.SetInt32("UserAvatar", user.Avatar);
            httpContext.Session.SetString("UserNickname", user.Nickname);
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

static async Task BroadcastRoomStatus(WebApplication app, HttpContext context, int tableIdx,
                                    GameTableDbContext gameTableDb) {
    var websockets = PlayingWebSockets.GetTableWebSockets(tableIdx);// Your loop logic here
    var data = await gameTableDb.Tables.FirstOrDefaultAsync(t => t.TableId == tableIdx);

    var jsonObject = new
    {
        Type = "BroadCast",
        Data = data // Use the object array here
    };

    // Serialize the object to a JSON string
    string jsonString = JsonSerializer.Serialize(jsonObject);
    app.Logger.LogInformation("We broadcast data " + jsonString);
    var messageBuffer = Encoding.UTF8.GetBytes(jsonString);
    if (websockets != null) {
        foreach (WebSocket? websocket in websockets) {
            if (websocket != null) {
                await websocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }
}
// Game Playing.
// verify Seat and table.
app.Map("/ws_playing", async (HttpContext context, GameTableDbContext db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var currentTable = context.Session.GetInt32("UserTableId");
        var currentPos = context.Session.GetInt32("UserTablePos");
        var userId = context.Session.GetInt32("UserId");
        var avatar =  context.Session.GetInt32("UserAvatar");
        var nickName = context.Session.GetString("UserNickname");


        app.Logger.LogInformation($"{currentTable} - {currentPos}");
        if (currentTable.HasValue && currentPos.HasValue) {
            PlayingWebSockets.AddSocket(currentTable.Value-1, currentPos.Value-1, webSocket);
        }

        var table = await db.Tables.FirstOrDefaultAsync(table => table.TableId == (currentTable??0));
        
        // create new player
        Player newPlayer = new Player {
            UserId = userId??0,
            AvatarId = avatar??5,
            Nickname = nickName??"Unknown",
            TableId = currentTable??0,
            Pos = currentPos??0,  
            GameTable = table??null,     
        };

       
        table?.Players.Add(newPlayer);
        app.Logger.LogInformation("RooWebsocket We add player " + nickName??"Unknown" + $" to table {currentTable??0}");


        await BroadcastRoomStatus(app, context, currentTable??1, db);
        await RoomWebSocketHandler(app, context, webSocket, db);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});

static async Task RoomWebSocketHandler(WebApplication app, HttpContext context, 
                                    WebSocket webSocket, GameTableDbContext gameTableDb)
{
    var buffer = new byte[1024 * 4];
    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    
    while (!result.CloseStatus.HasValue)
    {
        // Print the received content
        var receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
        app.Logger.LogInformation($"Received client message: {receivedMessage}"); // Log the received message


        var clientMessage = JsonSerializer.Deserialize<ClientMessage>(receivedMessage);


        if (clientMessage != null && clientMessage.Action == "IAMREADY") {
            
        }
        //await webSocket.SendAsync(new ArraySegment<byte>(replyMessageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            
        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }

    var currentTable = context.Session.GetInt32("UserTableId");
    var currentPos = context.Session.GetInt32("UserTablePos");

    app.Logger.LogInformation("RoomWebSocketHandler close websocket table " + $"{currentTable} pos {currentPos}");
    if (currentTable.HasValue && currentPos.HasValue) {
        PlayingWebSockets.RemoveSocket(currentTable.Value, currentPos.Value);
    }

    // Remove Player.
   
}

// GamePanel broadcast data.
// Take a Seat
// Define a WebSocket handler
app.Map("/ws_hall", async (HttpContext context, GameTableDbContext db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        ActiveWebSockets.AddSocket(context.Session.GetInt32("UserId")?? 0, webSocket);
        
        await BroadCastHallStatus(db, app.Logger);
        await BigHallWebSocketHandler(app, context, webSocket, db);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});


static async Task BigHallWebSocketHandler(WebApplication app, HttpContext context, 
                                    WebSocket webSocket, GameTableDbContext gameTableDb)
{
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

                    app.Logger.LogInformation("BigHall We add player " + clientMessage.NickName + " to table " + tableIdx);

                    table.Players.Add(newPlayer);

                    // clear old pos
                    var userTable = context.Session.GetInt32("UserTableId");
                    var userPos = context.Session.GetInt32("UserTablePos");
                    app.Logger.LogInformation("We get saved position " + userTable + "" + userPos);
                    if (userTable.HasValue && userPos.HasValue) {
                        var userId = context.Session.GetInt32("UserId");
                        var oldTable = await gameTableDb.Tables.FirstOrDefaultAsync(table => table.TableId == userTable);
                        if (oldTable != null) {
                            // Ensure the players are fetched from the database after any changes
                            await gameTableDb.Entry(oldTable).Collection(t => t.Players).LoadAsync(); // Load players again
                        }
                        // Check if table is not null before accessing Players
                        var oldPlayer = oldTable?.Players?.FirstOrDefault(p => p.Pos == userPos);
                        
                        if (oldPlayer != null) {
                            app.Logger.LogInformation("We clear position " + userTable + "" + userPos);
                            oldTable?.Players?.Remove(oldPlayer);
                        } else {
                            app.Logger.LogWarning("We failed clear position " + userTable + "" + userPos);
                        }

                        PlayingWebSockets.RemoveSocket(userTable??0, userPos??0);
                    }
                    // clear end.
                    context.Session.SetInt32("UserTableId", tableIdx);
                    context.Session.SetInt32("UserTablePos", pos);
                    app.Logger.LogInformation("We saved position " + tableIdx + "" + pos);
                    
                    gameTableDb.SaveChanges();
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

    // Client close it's socket here.
    ActiveWebSockets.RemoveSocket(context.Session.GetInt32("UserId")?? 0);
    var userTable1 = context.Session.GetInt32("UserTableId");
    var userPos1 = context.Session.GetInt32("UserTablePos");
    app.Logger.LogInformation("User close the websocket in table " + userTable1 + " pos " + userPos1);
    await BroadCastHallStatus(gameTableDb, app.Logger);
}

// New async method to run a loop
static async Task BroadCastHallStatus(GameTableDbContext gameTableDb, ILogger logger)
{
    // lock(ActiveWebSockets.condtionLockObject) {
    //     while (!ActiveWebSockets.conditionBroadCast)
    //     {
    //         Monitor.Wait(ActiveWebSockets.condtionLockObject); // 等待条件满足
    //     }
    // }

    var websockets = ActiveWebSockets.GetAllSockets();// Your loop logic here
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
    logger.LogInformation("We begin to broadcast data to all users " + websockets.Count + " " + jsonString);
    foreach (WebSocket websocket in websockets) {
        await websocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

Extensions.CreateDbIfNotExists(app);

app.Run();