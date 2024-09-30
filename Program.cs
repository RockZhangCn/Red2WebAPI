using Microsoft.EntityFrameworkCore;
using Red2WebAPI.Models;
using System.Text.Json; // Add this line

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
    config.DocumentName = "TodoAPI";
    config.Title = "TodoAPI v1";
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
// app.UseCors();

app.UseFileServer();
app.UseHsts();
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

app.UseSession(); // 启用 Session
app.UseWebSockets();


app.MapGet("/zhang", () => "Hello,Zhang");

app.MapGet("/todoitems", async (TodoDb db) =>
    await db.Todos.ToListAsync());

app.MapGet("/todoitems/complete", async (TodoDb db) =>
    await db.Todos.Where(t => t.IsComplete).ToListAsync());

app.MapGet("/todoitems/{id}", async (int id, TodoDb db) =>
    await db.Todos.FindAsync(id)
        is Todo todo
            ? Results.Ok(todo)
            : Results.NotFound());

app.MapPost("/register", async (Register user, UserDb db) =>
{
    db.Users.Add(user);
    app.Logger.LogInformation("New register user: " + JsonSerializer.Serialize(user));

    var userDto = new LoginUserDto
            {
                Email = user.Email,
                Nickname = user.Nickname,
                Avatar = user.Avatar,
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
        };
        
        httpContext.Session.SetString("UserId", user.Id.ToString());
        return Results.Ok(userDto); // Return the DTO
    }
   
    return Results.NotFound();
});

app.MapPut("/todoitems/{id}", async (int id, Todo inputTodo, TodoDb db) =>
{
    var todo = await db.Todos.FindAsync(id);

    if (todo is null) return Results.NotFound();

    todo.Name = inputTodo.Name;
    todo.IsComplete = inputTodo.IsComplete;

    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapDelete("/todoitems/{id}", async (int id, TodoDb db) =>
{
    if (await db.Todos.FindAsync(id) is Todo todo)
    {
        db.Todos.Remove(todo);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    return Results.NotFound();
});


app.Run();