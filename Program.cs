using Microsoft.EntityFrameworkCore;
using Red2WebAPI.Models;
using System.Text.Json; // Add this line
using System.Net.WebSockets; // Add this using directive
using System.Text; // Added for Encoding
using Red2WebAPI.Seed;
using Red2WebAPI.Communication;
using Cards;
using Microsoft.Extensions.Configuration.UserSecrets;


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

builder.WebHost.UseUrls("http://0.0.0.0:8003");

builder.Services.AddSqlite<UserDbContext>("Data Source=User.db");
builder.Services.AddDbContext<GameTableDbContext>(opt => opt.UseInMemoryDatabase("GameTable"));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowOrigin",
        builder => builder.WithOrigins("http://localhost:3000", "http://192.168.1.234:3000")
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
    options.Cookie.HttpOnly = true; // 只能通过 HTTP 访问
    options.Cookie.IsEssential = true; // 在 GDPR 下的设置

    options.IdleTimeout = TimeSpan.FromDays(1); // 设
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddLogging();
builder.Logging.AddConsole(); // Add this line to configure console logging

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; // Use camelCase
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // Ignore case
    });

var app = builder.Build();

// Ensure CORS middleware is applied before other middleware
app.UseCors("AllowOrigin");

// Add this line to handle 400 Bad Request and 404 Not Found
app.UseStatusCodePages(context =>
{
    var response = context.HttpContext.Response;
    response.ContentType = "text/plain";
    var path = context.HttpContext.Request.Path;


    if (response.StatusCode == 400)
    {
        return response.WriteAsync($"400 Bad Request {path}- The request could not be understood by the server.");
    }
    else if (response.StatusCode == 404)
    {
        return response.WriteAsync($"404 Not Found {path}- The resource you are looking for does not exist.");
    }

    return Task.CompletedTask; // For other status codes, do nothing
});


app.UseSession();
app.UseWebSockets();

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

app.MapGet("/", () => Results.Ok(new { Success=true, Message="Success."}));

app.MapGet("/scores", async (HttpContext httpContext, UserDbContext db) => {
     var userId = httpContext.Session.GetInt32("UserId");
     
     if (userId != null) {
        var usersData = await db.Users
            .Select(u => new {
                u.Email, // Specify the columns you want
                u.Score,
            })
            .ToListAsync();

        return Results.Ok(new { Success=true, Data=usersData});
     } else {
        return Results.Ok(new { Success=false, Message="Unauthorized access."});
     }
});


app.MapGet("/profile", (HttpContext httpContext, UserDbContext db) => {
     var userId = httpContext.Session.GetInt32("UserId");
     app.Logger.LogInformation("profile request get userId" + userId);
     if (userId != null) {
        var user = db.Users.FirstOrDefault(u => u.Id == userId);

        if (user != null) { 
            var userData = new {
                email=user.Email,
                score=user.Score,
                avatar=user.Avatar,
                nickname=user.Nickname,
                createtime=user.Createtime,

            };
            return Results.Ok(new { Success=true, Data=userData});
        }
     } 

     return Results.Ok(new { Success=false, Message="Unauthorized access."});
     
});

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

            var userId = httpContext.Session.GetInt32("UserId");
            ActiveWebSockets.RemoveSocket(userId?? 0);
            
            // clear your position in database.
            var userTable = getTableId(userId??0, gameTableDb);

            var userPos = getTablePos(userId??0, gameTableDb);

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
    user.Password = "";
    user.Createtime = DateTime.Now;

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
            app.Logger.LogInformation("Write user Id in session " + user.Id + "  get it " + httpContext.Session.GetInt32("UserId"));
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

static async Task BroadcastRoomStatus(WebApplication app, int tableIdx,
                                    GameTableDbContext gameTableDb) {
    
    await gameTableDb.Tables.ToListAsync();
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
                try {
                    await websocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                } catch(WebSocketException) {
                    int pos = PlayingWebSockets.GetWebsocketPostion(tableIdx, websocket);
                    app.Logger.LogError("SendAsyn Exception " + websocket.GetType().Name + " pos is " + pos);  
                }
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
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await RoomWebSocketHandler(app, context, webSocket, db);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});


static int getTableId(int UserId, GameTableDbContext db) {
    var player = db.Players.FirstOrDefault(p => p.UserId == UserId);
    return player?.TableId??0;
}

static int getTablePos(int UserId, GameTableDbContext db) {
    var player = db.Players.FirstOrDefault(p => p.UserId == UserId);
    return player?.Pos??0;
}


static async Task RoomWebSocketHandler(WebApplication app, HttpContext context, 
                                    WebSocket webSocket, GameTableDbContext gameTableDb1)
{
    var buffer = new byte[1024 * 4];
    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    
    var optionsBuilder = new DbContextOptionsBuilder<GameTableDbContext>(); // Use DbContextOptionsBuilder
    optionsBuilder.UseInMemoryDatabase("GameTable"); // Configure the in-memory database
    var gameTableDb = new GameTableDbContext(optionsBuilder.Options); // Pass the configured options

    var tableId = -1;
    int userPos = 0;
    
    while (!result.CloseStatus.HasValue)
    {
        gameTableDb = new GameTableDbContext(optionsBuilder.Options); 
        // Print the received content
        var receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
        app.Logger.LogInformation($"Received client room message: {receivedMessage}"); // Log the received message

        var clientMessage = JsonSerializer.Deserialize<ClientMessage>(receivedMessage);
        
        bool isPing = false;
        if(clientMessage?.Action == "PING") {
            clientMessage = null;
            isPing = true;
        } else {
            await gameTableDb.Tables.ToListAsync();
        }

        GameTable? curTable = null;
        Player? curPlayer = null;
        
        if (clientMessage != null) {
            tableId = clientMessage.TableIdx;
            curTable = await gameTableDb.Tables.FirstOrDefaultAsync(t => t.Id == tableId);
            if (curTable == null) {
                app.Logger.LogError("We received incorrect table");
                continue;
            }

            if (curTable.GameStatus == GameStatus.END) {
                curTable.GameStatus = GameStatus.WAITING;
                app.Logger.LogInformation("We switch game to WAITING due to last end.");
            }
            // make sure we have the latest data. ROCKROCKZHANG
            await gameTableDb.Entry(curTable).Collection(t => t.Players).LoadAsync(); //
            
            app.Logger.LogInformation("RoomWebSocketHandler curTable:" + JsonSerializer.Serialize<GameTable>(curTable));

            curPlayer = curTable.Players.FirstOrDefault(p => p.Pos == clientMessage.Pos); // Find the player by position
            if (curPlayer == null) {
                app.Logger.LogError($"We have an incorrect curPlayer with pos {clientMessage.Pos}");
                // The user didn't take seat, so redirect to bighall.
                
                var jsonObject = new
                {
                    Type = "RETURN",
                };

                // Serialize the object to a JSON string
                string jsonString = JsonSerializer.Serialize(jsonObject);

                var messageBuffer = Encoding.UTF8.GetBytes(jsonString);
                
                try {
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    return;
                } catch(WebSocketException) {
                    app.Logger.LogWarning("RoomWebSocketHandler SendAsync Exception " + webSocket.GetType().Name + " pos is " + clientMessage?.Pos);  
                }
            }

            userPos = clientMessage.Pos;

            bool noNeedMoveOn = clientMessage.Action == "YIELD2" || clientMessage.Action == "NOYIELD";

            int? prevActive = curTable.ActivePos;
            if (curTable.ActivePos != null && !noNeedMoveOn) {
                while(true) {
                    curTable.ActivePos = curTable.ActivePos % 4 + 1;
                    bool getEndedUser = false;
                    curTable.Players.ForEach(p => {
                        if (p.Pos ==  curTable.ActivePos && p.Cards?.Count == 0) {
                            getEndedUser = true;
                        }
                    });

                    if (!getEndedUser) {
                        break;
                    }
                }

                if (curTable.ActivePos == prevActive) {
                    app.Logger.LogWarning("Game end, we get the last user, but it notified too late.");
                }
            }

            if(clientMessage.Action == "IAMIN") {
                var currentTableId = clientMessage.TableIdx;
                var currentPosId = clientMessage.Pos;

                app.Logger.LogWarning($"Table Id is {currentTableId} pos id is {currentPosId}");
                var avatar =  context.Session.GetInt32("UserAvatar");
                var nickName = context.Session.GetString("UserNickname");

                
                PlayingWebSockets.AddSocket(currentTableId, currentPosId, webSocket);
                
                app.Logger.LogInformation($"ws_playing {nickName} PlayingWebSockets<{PlayingWebSockets.Count}> addED websocket for table {currentTableId} pos {currentPosId} END.");

                // The user take seat have added the Player, so we don't need add player here.
            } else if (clientMessage.Action == "IAMQUIT") { //backforward.
                app.Logger.LogInformation($"ws_playing some user {clientMessage.TableIdx} {clientMessage.Pos} exit the room");

                if (curPlayer != null) {
                    if (curTable.GameStatus != GameStatus.WAITING) {
                        app.Logger.LogWarning("We quit will exit total game, we have " + curPlayer.Cards?.Count + " cards.");
                        curTable.GameStatus = GameStatus.END;
                    }

                    curTable.Players.Remove(curPlayer);
                }
                
                app.Logger.LogInformation($"----------------------Removed player at position {clientMessage.Pos} from table {clientMessage.TableIdx}.");
                gameTableDb.SaveChanges();
                
                await BroadCastHallStatus(gameTableDb, app.Logger);

                PlayingWebSockets.RemoveSocket(clientMessage.TableIdx, clientMessage.Pos);
            } else if(clientMessage.Action == "IAMREADY") {
                if (curPlayer != null) {
                    curPlayer.Status = PlayerStatus.READY;
                    curPlayer.Message = "I am ready.";
                    curPlayer.Score = 0;
                }

                var notReadyPlayer = curTable.Players.FirstOrDefault(p => p.Status != PlayerStatus.READY);
                // we are all ready.
                if (notReadyPlayer == null && curTable.Players.Count == 4) {
                    app.Logger.LogInformation("AAAAAA We started the table game.");
                    curTable.NextScore = 4;
                    var all_cards = Card.Shuffle();
                    curTable.Players.ForEach(p => {
                        int index = p.Pos - 1;
                        p.Cards = all_cards.Skip(index * 26).Take(26).ToList(); // Corrected assignment to List<int>
                        if (p.Cards.Contains(48)) {
                            curTable.Red2TeamPos.Add(p.Pos);
                        }
                    });

                    curTable.GameStatus = GameStatus.GRAB2;
                    curTable.CentreCards.Clear();

                    Random random = new Random(); // Create a new instance of Random
                    curTable.ActivePos = random.Next(1, 5); // Generate a random number between 1 and 4 (inclusive)
                }

            } else if(clientMessage.Action == "GRAB2S") {
                foreach (var player in curTable.Players)
                {
                    while(player.Cards?.Contains(48)??false) {
                        player.Cards?.Remove(48);
                        app.Logger.LogInformation($"{player.Nickname} removed red 2");
                    }    
                }

                curTable.NextScore = 5;
                if (curPlayer != null) {
                    
                    curPlayer.Cards?.Add(48);
                    curPlayer.Cards?.Add(48);
                    app.Logger.LogInformation($"{curPlayer.Nickname} added red 2s");
                    curTable.ActivePos = curPlayer.Pos;
                    curTable.Red2TeamPos.Add(curPlayer.Pos);
                    curPlayer.Message = "Grab2, I'm the single.";
                }
                
                curTable.GameStatus = GameStatus.INPROGRESS;
            } else if(clientMessage.Action == "NOGRAB") {
                if (curPlayer != null) {
                    curPlayer.Status = PlayerStatus.NOGRAB;
                    curPlayer.Message = $"NoGrab, Next.";
                }

                var nograbUsers = curTable.Players.FindAll(x => x.Status == PlayerStatus.NOGRAB);
                if (nograbUsers.Count == 4) {
                    app.Logger.LogInformation("We are facing Yield 2, four users no grab already.");
                    bool shouldYield = false;
                    foreach (var player in curTable.Players)
                    {
                        var cards = player.Cards?.FindAll(x => x == 48);
                        if (cards?.Count == 2) {
                            shouldYield = true;
                        }
                        player.Status = PlayerStatus.INPROGRESS;
                    }

                    if (shouldYield) {
                        curTable.GameStatus = GameStatus.YIELD2;
                        curTable.NextScore = 5;
                    } else {
                        curTable.GameStatus = GameStatus.INPROGRESS;
                    }
                }

            } else if(clientMessage.Action == "YIELD2") {
                curPlayer?.Cards?.Remove(48);
                curTable.Players.ForEach(p => {
                    if (p.Pos == (curPlayer?.Pos + 1) % 4 + 1) {
                        p.Cards?.Add(48);
                        curTable.Red2TeamPos.Add(p.Pos);
                    }
                });
                curTable.NextScore = 4;
                curTable.GameStatus = GameStatus.INPROGRESS;
            } else if(clientMessage.Action == "NOYIELD") {
                curTable.GameStatus = GameStatus.INPROGRESS;
            } else if(clientMessage.Action == "SHOT") {
                curTable.CentreCards = clientMessage.Cards;
                curTable.CentreShotPlayerPos = clientMessage.Pos;
                
                foreach (var card in clientMessage.Cards) {
                    curPlayer?.Cards?.Remove(card);
                }

                if (curPlayer != null) {
                    curPlayer.Message = $"Played {clientMessage.Cards.Count} cards";
                }

                // calculate the Score.
                if (curPlayer?.Cards?.Count == 0) {
                    curPlayer.Score = curTable.NextScore;
                    app.Logger.LogInformation("We shot finished earned score " + curPlayer.Score);
                    curTable.NextScore--;
                    // We have no cards, move the right to next active user.
                    curTable.CentreShotPlayerPos = curTable.ActivePos;
                }

                // one person win the game.
                if (curPlayer?.Score >= 5) {
                    curTable.GameStatus = GameStatus.END;

                    if (curTable.Red2TeamPos.Contains(curPlayer.Pos)) {
                        curTable.Players.ForEach(p => {
                            p.Score = -10;
                        }); 

                        curPlayer.Score = 30;
                    } else {
                        curTable.Players.ForEach(p => {
                            p.Score = 10;
                        }); 

                        curPlayer.Score = -30;
                    } 
                } else {
                    // calculate our total socre.
                    int redScore = 0;
                    int nonRedScore = 0;
                    curTable.Players.ForEach(p => {
                        if (curTable.Red2TeamPos.Contains(p.Pos)) {
                            redScore += p.Score;
                        } else {
                            nonRedScore += p.Score;
                        }
                    });

                    if (redScore >= 5 || nonRedScore >= 5){
                        curTable.GameStatus = GameStatus.END;
                        if (redScore == 6) {
                            curTable.Players.ForEach(p => {
                                if (curTable.Red2TeamPos.Contains(p.Pos)) {
                                    p.Score = 5;
                                } else {
                                    p.Score = -5;
                                }
                            });

                        } else if (redScore == 7) {
                           curTable.Players.ForEach(p => {
                                if (curTable.Red2TeamPos.Contains(p.Pos)) {
                                    p.Score = 10;
                                } else {
                                    p.Score = -10;
                                }
                            });

                        } else if (nonRedScore == 6) {
                           curTable.Players.ForEach(p => {
                                if (curTable.Red2TeamPos.Contains(p.Pos)) {
                                    p.Score = -5;
                                } else {
                                    p.Score = 5;
                                }
                            });
                        } else if (nonRedScore == 7) {
                           curTable.Players.ForEach(p => {
                                if (curTable.Red2TeamPos.Contains(p.Pos)) {
                                    p.Score = -10;
                                } else {
                                    p.Score = 10;
                                }
                            });
                        } else if (redScore == 5 || nonRedScore == 5) { // It's a draw.
                            curTable.Players.ForEach(p => {
                                p.Score = 0;
                            });
                        }
                    }
                }
            } else if(clientMessage.Action == "SKIP") {
                // just switch to next.
                app.Logger.LogInformation("User clicked SKIP");
                if (curPlayer != null) {
                    curPlayer.Message = $"Skip, Next.";
                }
            } 

            // WAITING clear all the cards.
            if (curTable.GameStatus == GameStatus.END) {     
                app.Logger.LogWarning("***************************************Game end, clear all data.**************");
                curTable.Players.ForEach(p => {
                    p.Cards = [];
                    p.Status = PlayerStatus.SEATED;
                    p.Message = $"Game ends, get {p.Score} Points.";
                });
                curTable.CentreShotPlayerPos = 0;
                curTable.CentreCards.Clear();
                curTable.Red2TeamPos.Clear();
                curTable.ActivePos = null;

                var userDbBuilder = new DbContextOptionsBuilder<UserDbContext>(); // Use DbContextOptionsBuilder
                userDbBuilder.UseSqlite("Data Source=User.db");
                UserDbContext userDb = new UserDbContext(userDbBuilder.Options);

                curTable.Players.ForEach(p => {
                    var user = userDb.Users.FirstOrDefault(u => u.Id == p.UserId);
                    if (user != null) {
                        user.Score += p.Score;
                        app.Logger.LogInformation($"User <{user.Email}> get new <{user.Score}> points");
                    }
                });

                // save user score data
                userDb.SaveChanges();
            }

            app.Logger.LogInformation($"Current active post is {curTable.ActivePos}");
        }

        if (!isPing) {
            gameTableDb.SaveChanges();
            
            await BroadcastRoomStatus(app, tableId, gameTableDb);
        } else {
            var jsonObject = new
            {
                Type = "PONG",
            };

            // Serialize the object to a JSON string
            string jsonString = JsonSerializer.Serialize(jsonObject);

            var messageBuffer = Encoding.UTF8.GetBytes(jsonString);
            
            try {
                await webSocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            } catch(WebSocketException) {
                app.Logger.LogWarning("RoomWebSocketHandler SendAsync Exception " + webSocket.GetType().Name + " pos is " + clientMessage?.Pos);  
            }
        }

        try {
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        } catch(WebSocketException) {
            app.Logger.LogWarning($"ReceiveException User {curPlayer?.Nickname} pos {userPos} disconnected");
            break;
        }
    }

    var userId = context.Session.GetInt32("UserId");
    var currentTable = getTableId(userId??0, gameTableDb);
    var currentPos = getTablePos(userId??0, gameTableDb);

    gameTableDb = new GameTableDbContext(optionsBuilder.Options); 
    // If user directly close the tab, there will be no IAMQUIT message.
    app.Logger.LogWarning("User Close/Refresh Tab RoomWebSocketHandler close websocket table " + $"{currentTable} pos {currentPos}");
    PlayingWebSockets.RemoveSocket(currentTable, currentPos);

    var curTableObj = await gameTableDb.Tables.FirstOrDefaultAsync(t => t.Id == currentTable);
    if (curTableObj == null) {
        app.Logger.LogError("We received incorrect table");
        return;
    }

    // make sure we have the latest data. ROCKROCKZHANG
    await gameTableDb.Entry(curTableObj).Collection(t => t.Players).LoadAsync(); //

    var curPlayerObj = curTableObj.Players.FirstOrDefault(p => p.Pos == currentPos); // Find the player by position
    if (curPlayerObj == null) {
        app.Logger.LogError("We received incorrect pos");
        return;
    }

    curTableObj.Players.Remove(curPlayerObj); // Remove the player from the Players collection
    app.Logger.LogInformation($"----------------------Removed player {curPlayerObj.Nickname} at position {currentPos} from table {currentTable}.");
    gameTableDb.SaveChanges();

    var optionsBuilder1 = new DbContextOptionsBuilder<GameTableDbContext>(); // Use DbContextOptionsBuilder
    optionsBuilder1.UseInMemoryDatabase("GameTable"); // Configure the in-memory database
    var gameTableDb2 = new GameTableDbContext(optionsBuilder.Options); // Pass the configured options
    await BroadcastRoomStatus(app, currentTable, gameTableDb2);    

    await BroadCastHallStatus(gameTableDb2, app.Logger);

}

// GamePanel broadcast data.
// Take a Seat
// Define a WebSocket handler
app.Map("/ws_hall", async (HttpContext context, GameTableDbContext db) => // Added UserDb parameter
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        ActiveWebSockets.AddSocket(context.Session.GetInt32("UserId")?? 0, webSocket);
        
        //for first user to show Bighall.
        await BroadCastHallStatus(db, app.Logger);
        app.Logger.LogInformation("We connect through ws_hall");
        await BigHallWebSocketHandler(app, context, webSocket);
    }
    else
    {
        context.Response.StatusCode = 400; // Bad Request if not a WebSocket request
    }
});


static async Task BigHallWebSocketHandler(WebApplication app, HttpContext context, 
                                    WebSocket webSocket)
{
    var buffer = new byte[1024 * 4];
    var closeStatus = WebSocketCloseStatus.NormalClosure; // or any other appropriate status
    var result = new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, closeStatus, null); // Added null for closeStatusDescription

    try {
        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    } catch(WebSocketException){
        app.Logger.LogWarning("Exception user disconnect in BigHall");
    }
    
    var optionsBuilder = new DbContextOptionsBuilder<GameTableDbContext>(); // Use DbContextOptionsBuilder
    optionsBuilder.UseInMemoryDatabase("GameTable"); // Configure the in-memory database
    GameTableDbContext? gameTableDb = null;

    while (!result.CloseStatus.HasValue)
    {
        gameTableDb = new GameTableDbContext(optionsBuilder.Options); // Pass the configured options
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

                    app.Logger.LogInformation("+++++++++++BigHall We add player " + clientMessage.NickName + " to table " + tableIdx);
                    table.Players.Add(newPlayer);

                    app.Logger.LogInformation("We saved new position " + tableIdx + " - " + pos + " END.");
                    
                    gameTableDb.SaveChanges();
                    replyResult = true;

                    // room broadcast notify other user we come in.
                    // add a action(We are in) to remove this broadcast.

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

        
        try {
            await webSocket.SendAsync(new ArraySegment<byte>(replyMessageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
        } catch(WebSocketException e) {
            app.Logger.LogError("BigHall Handler SendAsync Exception " + webSocket.GetType().Name, e);
        }
        
        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }

    // Client close it's socket here.
    ActiveWebSockets.RemoveSocket(context.Session.GetInt32("UserId")?? 0);
    gameTableDb = new GameTableDbContext(optionsBuilder.Options); // Pass the configured options
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
    logger.LogInformation("BigHall begin to broadcast data to all <" + websockets.Count + "> users :" + jsonString);
    foreach (WebSocket websocket in websockets) {
        try {
            await websocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
        } catch(WebSocketException e) {
            logger.LogError("BroadCast Hall SendAsync Exception " + websocket.GetType().Name, e);
        }
    }
}

Extensions.CreateDbIfNotExists(app);

app.Run();

