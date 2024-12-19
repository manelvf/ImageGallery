using ImageGallery.Components;
using System.Data.SQLite;
using Microsoft.Extensions.FileProviders;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 3 * 1024 * 1024; // 3 MB
});


builder.Services.AddHttpClient("APIClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5102");
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "data")),
    RequestPath = "/data"
});

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();


string connectionString = "Data Source=images.db;Version=3;";

string createTableQuery = @"
    CREATE TABLE IF NOT EXISTS Images (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL,
        Password TEXT NOT NULL,
        InsertionDate DATETIME NOT NULL
    );";

using (var connection = new SQLiteConnection(connectionString))
{
    connection.Open();
    Console.WriteLine("SQLite database connected successfully.");
    
    using (var command = new SQLiteCommand(createTableQuery, connection))
    {
        command.ExecuteNonQuery();
        Console.WriteLine("Table created successfully.");
    }
}

app.Run();