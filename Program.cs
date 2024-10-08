using Microsoft.EntityFrameworkCore;
using Red2WebAPI.Models;
using System.Text.Json; // Add this line
using System.Net.WebSockets; // Add this using directive
using System.Text; // Added for Encoding
using Red2WebAPI.Seed;
using Red2WebAPI.Communication;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Cards;
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
                    app.Logger.LogInformation("----------- User logout, we clear position " + userTable + "" + userPos);
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

static async Task BroadcastRoomStatus(WebApplication app, HttpContext context,
                                    GameTableDbContext gameTableDb) {
    
    var tableIdx = context.Session.GetInt32("UserTableId")??0;
    var currentTable = await gameTableDb.Tables.FirstOrDefaultAsync(t => t.TableId == tableIdx);
    if (currentTable != null) {
        // Ensure the players are fetched from the database after any changes
        // ROCK ZHANG.
        await gameTableDb.Entry(currentTable).Collection(t => t.Players).LoadAsync(); // Load players again
    }

    var jsonObject = new
    {
        Type = "BroadCast",
        Data = currentTable // Use the object array here
    };

    // Serialize the object to a JSON string
    string jsonString = JsonSerializer.Serialize(jsonObject);
    var messageBuffer = Encoding.UTF8.GetBytes(jsonString);
    var websockets = PlayingWebSockets.GetTableWebSockets(tableIdx);
    
    app.Logger.LogInformation($"ROOM BroadCast table Id {tableIdx} users data {jsonString} socket count {PlayingWebSockets.Count}");

    if (websockets != null) {
        foreach (WebSocket? websocket in websockets) {
            if (websocket != null) {
                app.Logger.LogInformation("ROOM BroadCast send data.");
                await websocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    } else {
        app.Logger.LogWarning("ROOM BroadCast has not available websockets.");
    }
}
// Game Playing.
// verify Seat and table.
app.Map("/ws_playing", async (HttpContext context, GameTableDbContext db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

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
        app.Logger.LogInformation($"Received client room message: {receivedMessage}"); // Log the received message


        var clientMessage = JsonSerializer.Deserialize<ClientMessage>(receivedMessage);

        if (clientMessage != null) {
            var curTable = await gameTableDb.Tables.FirstOrDefaultAsync(t => t.Id == clientMessage.TableIdx);
            if (curTable == null) {
                app.Logger.LogError("We received incorrect table");
                continue;
            }

            // make sure we have the latest data. ROCKROCKZHANG
            await gameTableDb.Entry(curTable).Collection(t => t.Players).LoadAsync(); //
            


            if(clientMessage.Action == "IAMIN") {
                // added to ROOM broacast.
                var currentTableId = context.Session.GetInt32("UserTableId");
                var currentPosId = context.Session.GetInt32("UserTablePos");
                var userId = context.Session.GetInt32("UserId");
                var avatar =  context.Session.GetInt32("UserAvatar");
                var nickName = context.Session.GetString("UserNickname");

                if (currentTableId.HasValue && currentPosId.HasValue) {
                    PlayingWebSockets.AddSocket(currentTableId.Value, currentPosId.Value, webSocket);
                }
                app.Logger.LogInformation($"ws_playing {nickName} PlayingWebSockets<{PlayingWebSockets.Count}> addED websocket for table {currentTableId} pos {currentPosId} END.");

                // The user take seat have added the Player, so we don't need add player here.
            } 
            
            var curPlayer = curTable.Players.FirstOrDefault(p => p.Pos == clientMessage.Pos); // Find the player by position
            if (curPlayer == null) {
                app.Logger.LogError($"We have an incorrect curPlayer with pos {clientMessage.Pos}");
                continue;
            }

            if(clientMessage.Action == "IAMQUIT") {
                app.Logger.LogInformation($"ws_playing some user {clientMessage.TableIdx} {clientMessage.Pos} exit the room");

                // Clear data in session.
                context.Session.Remove("UserTableId");
                context.Session.Remove("UserTablePos");

                
                curTable.Players.Remove(curPlayer);
                
                app.Logger.LogInformation($"----------------------Removed player at position {clientMessage.Pos} from table {clientMessage.TableIdx}.");
                gameTableDb.SaveChanges();
                
                await BroadCastHallStatus(gameTableDb, app.Logger);

                PlayingWebSockets.RemoveSocket(clientMessage.TableIdx, clientMessage.Pos);
            } else if(clientMessage.Action == "IAMREADY") {
  
                curPlayer.Status = PlayerStatus.READY;
                curPlayer.Message = "I am ready.";
  

                var notReadyPlayer = curTable.Players.FirstOrDefault(p => p.Status != PlayerStatus.SEATED);

                // we are all ready.
                if (notReadyPlayer == null) {
                    app.Logger.LogInformation("AAAAAA We started the table game.");

                    var all_cards = Card.Shuffle();
                    curTable.Players.ForEach(p => {
                        int index = p.Pos - 1;
                        p.Cards = all_cards.Skip(index * 26).Take(26).ToList(); // Corrected assignment to List<int>
                    });

                    foreach (var player in curTable.Players)
                    {
                        player.Status = PlayerStatus.INPROGRESS; // Set the desired status here
                    }
                    curTable.GameStatus = GameStatus.GRAB2;

                    // last user have the active.

                    curPlayer.IsActive = true;
                }  
            } 
            
            // Current is the active player.
            var nextActivePos =(curPlayer.Pos + 1)%5;
            foreach (var player in curTable.Players)
            {
                if (player.Pos == nextActivePos) {
                    player.IsActive = true;
                } else {
                    player.IsActive = false;
                }
            }
            
            if(clientMessage.Action == "GRAB2S") {
                foreach (var player in curTable.Players)
                {
                    while(player.Cards?.Contains(48)??false) {
                        player.Cards?.Remove(48);
                    }
                    player.IsActive = false;
                }

                curPlayer.Cards?.Add(48);
                curPlayer.Cards?.Add(48);
                curPlayer.IsActive = true;

                curTable.GameStatus = GameStatus.INPROGRESS;
            } else if(clientMessage.Action == "NOGRAB") {
                curTable.GameStatus = GameStatus.GRAB2;
            } else if(clientMessage.Action == "YIELD2") {
                curPlayer.Cards?.Remove(48);
                curTable.Players.ForEach(p => {
                    if (p.Pos == (curPlayer?.Pos + 2)%5) {
                        p.Cards?.Add(48);
                    }
                });

                curTable.GameStatus = GameStatus.INPROGRESS;
            } else if(clientMessage.Action == "SHOT") {
                curTable.CentreCards = clientMessage.Cards;
            } else if(clientMessage.Action == "SKIP") {

            }
        }
        
        gameTableDb.SaveChanges();
        //await webSocket.SendAsync(new ArraySegment<byte>(replyMessageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);  
        
        await BroadcastRoomStatus(app, context, gameTableDb);

        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }

    var currentTable = context.Session.GetInt32("UserTableId");
    var currentPos = context.Session.GetInt32("UserTablePos");

    if (currentTable.HasValue && currentPos.HasValue) {
        // If user directly close the tab, there will be no IAMQUIT message.
        app.Logger.LogInformation("User Close Tab RoomWebSocketHandler close websocket table " + $"{currentTable} pos {currentPos}");
        PlayingWebSockets.RemoveSocket(currentTable.Value, currentPos.Value);

        var curTable = await gameTableDb.Tables.FirstOrDefaultAsync(t => t.Id == currentTable.Value);
        if (curTable == null) {
            app.Logger.LogError("We received incorrect table");
            return;
        }

        // make sure we have the latest data. ROCKROCKZHANG
        await gameTableDb.Entry(curTable).Collection(t => t.Players).LoadAsync(); //

        var curPlayer = curTable.Players.FirstOrDefault(p => p.Pos == currentPos.Value); // Find the player by position
        if (curPlayer == null) {
            app.Logger.LogError("We received incorrect pos");
            return;
        }

        curTable.Players.Remove(curPlayer); // Remove the player from the Players collection
        app.Logger.LogInformation($"----------------------Removed player at position {currentPos.Value} from table {currentTable.Value}.");
        gameTableDb.SaveChanges();

        // Clear data in session.
        context.Session.Remove("UserTableId");
        context.Session.Remove("UserTablePos");

        await BroadCastHallStatus(gameTableDb, app.Logger);
    }
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
        app.Logger.LogInformation($"Received client bighall message: {receivedMessage}"); // Log the received message
     
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
                        Status = PlayerStatus.SEATED,
                        GameTable = table,
                        Message = "Just seated."
                    };

                    // clear old pos
                    var userTable = context.Session.GetInt32("UserTableId");
                    var userPos = context.Session.GetInt32("UserTablePos");
                    app.Logger.LogInformation("We get saved position " + userTable + " - " + userPos + " END.");
                    if (userTable.HasValue && userPos.HasValue) {
                        var userId = context.Session.GetInt32("UserId");
                        var oldTable = await gameTableDb.Tables.FirstOrDefaultAsync(table => table.TableId == userTable);
                        if (oldTable != null) {
                            // Ensure the players are fetched from the database after any changes
                            // ROCK ZHANG.
                            await gameTableDb.Entry(oldTable).Collection(t => t.Players).LoadAsync(); // Load players again
                        }
                        // Check if table is not null before accessing Players
                        var oldPlayer = oldTable?.Players?.FirstOrDefault(p => p.Pos == userPos);
                        
                        if (oldPlayer != null) {
                            app.Logger.LogInformation("----------- We clear position " + userTable + "" + userPos);
                            oldTable?.Players?.Remove(oldPlayer);
                        } else {
                            app.Logger.LogWarning("We failed clear position " + userTable + "" + userPos);
                        }

                        // no need, just check the room websocket.
                        //PlayingWebSockets.RemoveSocket(userTable??0, userPos??0);
                    }
                    // clear end.
             
                    app.Logger.LogInformation("+++++++++++BigHall We add player " + clientMessage.NickName + " to table " + tableIdx);
                    table.Players.Add(newPlayer);

                    context.Session.SetInt32("UserTableId", tableIdx);
                    context.Session.SetInt32("UserTablePos", pos);
                    app.Logger.LogInformation("We saved new position " + tableIdx + " - " + pos + " END.");
                    
                    gameTableDb.SaveChanges();
                    replyResult = true;

                    // room broadcast notify other user we come in.
                    // add a action(We are in) to remove this broadcast.
                    // await BroadcastRoomStatus(app, context, gameTableDb);

                } else {
                    replyResult = false;
                    replyMsg = "Alread seated.";
                }

            } else {
                replyResult = false;
                app.Logger.LogError($"We received an invalid table id {tableIdx}");
                replyMsg = "Invalid Table.";
            }

            var replyObj = new ServerMessage{
                Type = "REPLY",
                Result = replyResult,
                Message = replyMsg,
            };

            replyJsonString = JsonSerializer.Serialize(replyObj);
        } else {
            // never here.
            var replyObj = new ServerMessage{
                Type = "REPLY",
                Result = false,
                Message = "We received an unknown message.",
            };

            replyJsonString = JsonSerializer.Serialize(replyObj);

            app.Logger.LogError("We received an unknown message.");
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
    app.Logger.LogInformation("User close the bighall websocket into table " + userTable1 + " pos " + userPos1);
    await BroadCastHallStatus(gameTableDb, app.Logger);
}

// New async method to run a loop
static async Task BroadCastHallStatus(GameTableDbContext gameTableDb, ILogger logger)
{
    var websockets = ActiveWebSockets.GetAllSockets();// Your loop logic here

    //var table = await gameTableDb.Tables.ToArrayAsync();

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
